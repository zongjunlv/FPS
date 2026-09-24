"""Persistent, self-hosted account and two-player room state for FPS.

The SQLite file belongs under /srv/fps/data, never a versioned release.
All mutations use BEGIN IMMEDIATE and serialize through one process lock.
"""

from __future__ import annotations

import hashlib
import hmac
import json
import pathlib
import secrets
import sqlite3
import threading
import time
import unicodedata
from contextlib import contextmanager
from datetime import datetime, timezone
from typing import Any, Iterator


SCHEMA_VERSION = 1
PASSWORD_ITERATIONS = 600_000
TOKEN_SECONDS = 24 * 3600
ROOM_NAME_LIMIT = 48
MAP_ID = "CityNew"


class ConflictError(RuntimeError):
    pass


def utc(epoch: float | None = None) -> str:
    return datetime.fromtimestamp(time.time() if epoch is None else epoch,
                                  timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def _password(password: Any) -> str:
    if not isinstance(password, str) or not 8 <= len(password) <= 30 or \
            not any(c.isalpha() for c in password) or \
            not any(c.isdigit() for c in password):
        raise ValueError("密码须为 8—30 个字符且至少包含字母和数字")
    return password


def _username(value: Any) -> str:
    if not isinstance(value, str):
        raise ValueError("账号名无效")
    normalized = unicodedata.normalize("NFC", value)
    if not 3 <= len(normalized) <= 20 or not all(
            c.isalpha() or c.isdigit() or c in ".-@_" for c in normalized):
        raise ValueError("账号须为 3—20 位字母、数字或 .-@_")
    return normalized


def _appearance(value: Any) -> str:
    if not isinstance(value, str) or not 1 <= len(value) <= 64 or not all(
            c.isascii() and (c.isalnum() or c in "._-") for c in value):
        raise ValueError("角色外观无效")
    return value


class ControlStore:
    def __init__(self, database: pathlib.Path, token_seconds: int = TOKEN_SECONDS) -> None:
        self.database = pathlib.Path(database)
        if not self.database.is_absolute():
            raise ValueError("数据库路径必须是绝对路径，并位于持久化数据目录")
        self.database.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.lock = threading.RLock()
        self.token_seconds = token_seconds
        self.db = sqlite3.connect(str(self.database), timeout=10,
                                  isolation_level=None, check_same_thread=False)
        self.database.chmod(0o600)
        self.db.row_factory = sqlite3.Row
        self.db.execute("PRAGMA foreign_keys=ON")
        self.db.execute("PRAGMA busy_timeout=10000")
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.execute("PRAGMA synchronous=FULL")
        self._migrate()

    def close(self) -> None:
        with self.lock:
            self.db.close()

    def _migrate(self) -> None:
        with self.lock:
            version = self.db.execute("PRAGMA user_version").fetchone()[0]
            if version > SCHEMA_VERSION:
                raise RuntimeError("数据库版本高于当前服务，拒绝启动以保护数据")
            if version == 0:
                self.db.executescript("""BEGIN IMMEDIATE;
                    CREATE TABLE accounts (
                      id TEXT PRIMARY KEY, username TEXT NOT NULL,
                      username_key TEXT NOT NULL UNIQUE,
                      password_salt BLOB NOT NULL,
                      password_hash BLOB NOT NULL,
                      password_iterations INTEGER NOT NULL,
                      created_at TEXT NOT NULL);
                    CREATE TABLE access_tokens (
                      token_hash BLOB PRIMARY KEY,
                      account_id TEXT NOT NULL REFERENCES accounts(id),
                      expires_at INTEGER NOT NULL,
                      revoked INTEGER NOT NULL DEFAULT 0);
                    CREATE INDEX idx_tokens_account ON access_tokens(account_id);
                    CREATE TABLE rooms (
                      id TEXT PRIMARY KEY, join_code TEXT NOT NULL UNIQUE,
                      display_name TEXT NOT NULL, map_id TEXT NOT NULL,
                      host_id TEXT NOT NULL REFERENCES accounts(id),
                      phase TEXT NOT NULL, seed INTEGER NOT NULL,
                      load_epoch TEXT NOT NULL DEFAULT '',
                      load_failure TEXT NOT NULL DEFAULT '',
                      server_allocation TEXT NOT NULL DEFAULT '',
                      updated_at TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1);
                    CREATE TABLE room_players (
                      room_id TEXT NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
                      account_id TEXT NOT NULL REFERENCES accounts(id),
                      appearance_id TEXT NOT NULL,
                      ready INTEGER NOT NULL DEFAULT 0,
                      ready_epoch TEXT NOT NULL DEFAULT '',
                      connected INTEGER NOT NULL DEFAULT 1,
                      PRIMARY KEY(room_id,account_id));
                    CREATE INDEX idx_room_players_account ON room_players(account_id);
                    PRAGMA user_version=1;
                    COMMIT;
                """)

    @contextmanager
    def _write(self) -> Iterator[None]:
        with self.lock:
            self.db.execute("BEGIN IMMEDIATE")
            try:
                yield
            except BaseException:
                self.db.execute("ROLLBACK")
                raise
            else:
                self.db.execute("COMMIT")

    def register(self, username: Any, password: Any) -> dict[str, Any]:
        username = _username(username)
        password = _password(password)
        account = "acc-" + secrets.token_hex(16)
        salt = secrets.token_bytes(32)
        digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"),
                                     salt, PASSWORD_ITERATIONS)
        with self._write():
            try:
                self.db.execute("INSERT INTO accounts VALUES(?,?,?,?,?,?,?)",
                                (account, username, username.casefold(), salt,
                                 digest, PASSWORD_ITERATIONS, utc()))
            except sqlite3.IntegrityError as error:
                raise ConflictError("账号名已被使用") from error
            return self._issue_token(account, username)

    def login(self, username: Any, password: Any) -> dict[str, Any]:
        try:
            username = _username(username)
        except ValueError as error:
            raise PermissionError("账号或密码错误") from error
        if not isinstance(password, str):
            raise PermissionError("账号或密码错误")
        with self._write():
            row = self.db.execute(
                "SELECT * FROM accounts WHERE username_key=?",
                (username.casefold(),)).fetchone()
            # Equal-cost failure path avoids easy username enumeration.
            salt = row["password_salt"] if row else b"\0" * 32
            iterations = row["password_iterations"] if row else PASSWORD_ITERATIONS
            digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"),
                                         salt, iterations)
            if row is None or not hmac.compare_digest(digest, row["password_hash"]):
                raise PermissionError("账号或密码错误")
            return self._issue_token(row["id"], row["username"])

    def _issue_token(self, account: str, username: str) -> dict[str, Any]:
        token = secrets.token_urlsafe(48)
        expires = int(time.time()) + self.token_seconds
        self.db.execute("DELETE FROM access_tokens WHERE revoked=1 OR expires_at<=?",
                        (int(time.time()),))
        self.db.execute("INSERT INTO access_tokens VALUES(?,?,?,0)",
                        (hashlib.sha256(token.encode()).digest(), account, expires))
        return {"accountId": account, "username": username,
                "accessToken": token, "expiresAtUtc": utc(expires)}

    def authenticate(self, token: str) -> dict[str, str]:
        if not token or len(token) > 256:
            raise PermissionError("登录令牌无效")
        with self.lock:
            row = self.db.execute("""
                SELECT a.id,a.username FROM access_tokens t
                JOIN accounts a ON a.id=t.account_id
                WHERE t.token_hash=? AND t.revoked=0 AND t.expires_at>?
            """, (hashlib.sha256(token.encode()).digest(), int(time.time()))).fetchone()
        if row is None:
            raise PermissionError("登录令牌无效或已过期")
        return {"accountId": row["id"], "username": row["username"]}

    def logout(self, token: str) -> None:
        with self._write():
            self.db.execute("UPDATE access_tokens SET revoked=1 WHERE token_hash=?",
                            (hashlib.sha256(token.encode()).digest(),))

    def _room(self, room_id: str) -> dict[str, Any]:
        row = self.db.execute("SELECT * FROM rooms WHERE id=?", (room_id,)).fetchone()
        if row is None:
            raise LookupError("房间不存在")
        players = self.db.execute("""
            SELECT account_id,appearance_id,ready,ready_epoch,connected
            FROM room_players WHERE room_id=?
            ORDER BY CASE WHEN account_id=? THEN 0 ELSE 1 END, rowid
        """, (room_id, row["host_id"])).fetchall()
        return {"id": row["id"], "joinCode": row["join_code"],
                "displayName": row["display_name"], "mapId": row["map_id"],
                "hostId": row["host_id"], "phase": row["phase"],
                "seed": row["seed"], "loadEpoch": row["load_epoch"],
                "loadFailure": row["load_failure"],
                "serverAllocation": json.loads(row["server_allocation"])
                                    if row["server_allocation"] else None,
                "players": [{"accountId": p["account_id"],
                             "appearanceId": p["appearance_id"],
                             "ready": bool(p["ready"]),
                             "readyEpoch": p["ready_epoch"],
                             "connected": bool(p["connected"])} for p in players],
                "maximumPlayers": 2, "updatedAtUtc": row["updated_at"],
                "revision": row["revision"]}

    def get_room(self, room_id: str, account: str | None = None) -> dict[str, Any]:
        with self.lock:
            room = self._room(room_id)
        if account and not any(p["accountId"] == account for p in room["players"]):
            raise PermissionError("当前账号不是房间成员")
        return room

    def current_room(self, account: str) -> dict[str, Any] | None:
        with self.lock:
            row = self.db.execute("""
                SELECT r.id FROM rooms r
                JOIN room_players p ON p.room_id=r.id
                WHERE p.account_id=? AND r.phase!='cancelled'
                ORDER BY r.updated_at DESC LIMIT 1
            """, (account,)).fetchone()
            return self._room(row["id"]) if row else None

    def list_rooms(self) -> list[dict[str, Any]]:
        with self.lock:
            ids = [r[0] for r in self.db.execute(
                """SELECT r.id FROM rooms r WHERE r.phase='lobby'
                   AND (SELECT COUNT(*) FROM room_players p WHERE p.room_id=r.id)<2
                   ORDER BY r.updated_at DESC LIMIT 100""")]
            return [{"id": room["id"], "displayName": room["displayName"],
                     "mapId": room["mapId"], "playerCount": len(room["players"]),
                     "maximumPlayers": room["maximumPlayers"],
                     "updatedAtUtc": room["updatedAtUtc"], "phase": room["phase"]}
                    for room in (self._room(room_id) for room_id in ids)]

    def recover_orphaned_matches(self, active_session_id: str | None,
                                 active_match_id: str | None) -> list[str]:
        """Keep accounts and rosters, but discard battles with no live server.

        Called at broker startup and before room reads, so a migrated or
        crashed server cannot strand clients in a loading/battle phase.
        """
        recovered: list[str] = []
        with self._write():
            rows = self.db.execute(
                "SELECT id,server_allocation FROM rooms WHERE phase IN ('loading','battle')"
            ).fetchall()
            for row in rows:
                room_id = row["id"]
                allocation = json.loads(row["server_allocation"]) if row[
                    "server_allocation"] else {}
                if room_id == active_session_id and active_match_id and \
                        allocation.get("matchId") == active_match_id:
                    continue
                self.db.execute("""UPDATE rooms SET phase='lobby',load_epoch='',
                    load_failure='',server_allocation='' WHERE id=?""",
                    (room_id,))
                self.db.execute("""UPDATE room_players SET ready=0,
                    ready_epoch='' WHERE room_id=?""", (room_id,))
                self._touch(room_id)
                recovered.append(room_id)
        return recovered

    def _touch(self, room_id: str) -> None:
        self.db.execute("UPDATE rooms SET updated_at=?,revision=revision+1 WHERE id=?",
                        (utc(), room_id))

    def _one_active_room(self, account: str) -> bool:
        return self.db.execute("""
            SELECT 1 FROM room_players p JOIN rooms r ON r.id=p.room_id
            WHERE p.account_id=? AND r.phase!='cancelled' LIMIT 1
        """, (account,)).fetchone() is not None

    def create_room(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        name = payload.get("displayName")
        if not isinstance(name, str) or not name.strip() or len(name) > ROOM_NAME_LIMIT:
            raise ValueError("房间名称须为 1—48 个字符")
        if payload.get("mapId") != MAP_ID:
            raise ValueError("当前只支持 CityNew 地图")
        appearance = _appearance(payload.get("appearanceId"))
        seed = int(payload.get("seed") or 18018)
        if not 0 <= seed <= 2_147_483_647:
            raise ValueError("随机种子无效")
        with self._write():
            if self._one_active_room(account):
                raise ConflictError("请先退出当前房间")
            room_id = "room-" + secrets.token_hex(12)
            while True:
                code = "".join(secrets.choice("ABCDEFGHJKLMNPQRSTUVWXYZ23456789")
                               for _ in range(8))
                if self.db.execute("SELECT 1 FROM rooms WHERE join_code=?",
                                   (code,)).fetchone() is None:
                    break
            self.db.execute("""INSERT INTO rooms
                (id,join_code,display_name,map_id,host_id,phase,seed,updated_at)
                VALUES(?,?,?,?,?,'lobby',?,?)""",
                (room_id, code, name.strip(), MAP_ID, account, seed, utc()))
            self.db.execute("INSERT INTO room_players(room_id,account_id,appearance_id) VALUES(?,?,?)",
                            (room_id, account, appearance))
            return self._room(room_id)

    def join_room(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        room_id, code = payload.get("sessionId"), payload.get("joinCode")
        if isinstance(room_id, str):
            room_id = room_id.strip() or None
        if isinstance(code, str):
            code = code.strip().upper() or None
        if not room_id and not code:
            raise ValueError("缺少房间 ID 或加入码")
        if room_id is not None and (not isinstance(room_id, str) or
                                    not 1 <= len(room_id) <= 128):
            raise ValueError("房间 ID 无效")
        if code is not None and (not isinstance(code, str) or len(code) != 8):
            raise ValueError("加入码无效")
        appearance = _appearance(payload.get("appearanceId"))
        with self._write():
            row = self.db.execute("SELECT id FROM rooms WHERE id=? OR join_code=?",
                                  (room_id, code)).fetchone()
            if row is None:
                raise LookupError("房间不存在")
            room = self._room(row["id"])
            existing = next((p for p in room["players"] if p["accountId"] == account), None)
            if existing:
                self.db.execute("UPDATE room_players SET connected=1 WHERE room_id=? AND account_id=?",
                                (room["id"], account))
                self._touch(room["id"])
                return self._room(room["id"])
            if room["phase"] != "lobby" or len(room["players"]) >= 2:
                raise ConflictError("房间已开始或人数已满")
            if self._one_active_room(account):
                raise ConflictError("请先退出当前房间")
            self.db.execute("INSERT INTO room_players(room_id,account_id,appearance_id) VALUES(?,?,?)",
                            (room["id"], account, appearance))
            self._touch(room["id"])
            return self._room(room["id"])

    def update_player(self, room_id: str, account: str,
                      payload: dict[str, Any]) -> dict[str, Any]:
        with self._write():
            room = self._room(room_id)
            if not any(p["accountId"] == account for p in room["players"]):
                raise PermissionError("当前账号不是房间成员")
            if "appearanceId" in payload:
                if room["phase"] != "lobby":
                    raise ConflictError("战局开始后不能更换角色")
                self.db.execute("UPDATE room_players SET appearance_id=? WHERE room_id=? AND account_id=?",
                                (_appearance(payload["appearanceId"]), room_id, account))
            if "ready" in payload:
                if room["phase"] != "lobby" or not isinstance(payload["ready"], bool):
                    raise ConflictError("当前不能修改准备状态")
                self.db.execute("UPDATE room_players SET ready=? WHERE room_id=? AND account_id=?",
                                (int(payload["ready"]), room_id, account))
            if "readyEpoch" in payload:
                if room["phase"] != "loading" or payload["readyEpoch"] != room["loadEpoch"]:
                    raise ConflictError("加载确认与当前战局不匹配")
                self.db.execute("UPDATE room_players SET ready_epoch=? WHERE room_id=? AND account_id=?",
                                (room["loadEpoch"], room_id, account))
            self._touch(room_id)
            return self._room(room_id)

    def set_phase(self, room_id: str, account: str,
                  payload: dict[str, Any]) -> dict[str, Any]:
        phase = payload.get("phase")
        if phase not in ("battle", "cancelled", "lobby"):
            raise ValueError("房间阶段无效")
        with self._write():
            room = self._room(room_id)
            if room["hostId"] != account:
                raise PermissionError("只有房主可以更改房间阶段")
            if phase == "battle":
                if room["phase"] != "loading" or not room["serverAllocation"] or \
                        not all(p["readyEpoch"] == room["loadEpoch"] for p in room["players"]):
                    raise ConflictError("成员尚未完成加载或服务器未分配")
                self.db.execute("UPDATE rooms SET phase='battle',load_failure='' WHERE id=?", (room_id,))
            elif phase == "cancelled":
                if room["phase"] not in ("loading", "battle"):
                    raise ConflictError("当前没有可取消的战局")
                failure = payload.get("loadFailure") or "战局已取消"
                if not isinstance(failure, str) or len(failure) > 256:
                    raise ValueError("失败说明过长")
                self.db.execute("UPDATE rooms SET phase='cancelled',load_failure=? WHERE id=?",
                                (failure, room_id))
            else:
                self.db.execute("""UPDATE rooms SET phase='lobby',load_epoch='',
                    load_failure='',server_allocation='' WHERE id=?""", (room_id,))
                self.db.execute("UPDATE room_players SET ready=0,ready_epoch='' WHERE room_id=?", (room_id,))
            self._touch(room_id)
            return self._room(room_id)

    def mark_allocated(self, room_id: str, account: str,
                       allocation: dict[str, Any],
                       expected_revision: int,
                       expected_roster: list[dict[str, str]]) -> dict[str, Any]:
        with self._write():
            room = self._room(room_id)
            if room["hostId"] != account or room["phase"] != "lobby" or \
                    not room["players"] or not all(p["ready"] for p in room["players"]):
                raise PermissionError("只有全员准备的房主可以分配战局")
            roster = [{"accountId": p["accountId"],
                       "appearanceId": p["appearanceId"]}
                      for p in room["players"]]
            if room["revision"] != expected_revision or roster != expected_roster:
                raise ConflictError("房间成员或准备状态已变化，请重新开始")
            self.db.execute("""UPDATE rooms SET phase='loading',load_epoch=?,
                load_failure='',server_allocation=? WHERE id=?""",
                (secrets.token_hex(16), json.dumps(allocation), room_id))
            self.db.execute("UPDATE room_players SET ready_epoch='' WHERE room_id=?", (room_id,))
            self._touch(room_id)
            return self._room(room_id)

    def leave_room(self, room_id: str, account: str) -> dict[str, bool]:
        with self._write():
            room = self._room(room_id)
            if not any(p["accountId"] == account for p in room["players"]):
                raise PermissionError("当前账号不是房间成员")
            self.db.execute("DELETE FROM room_players WHERE room_id=? AND account_id=?",
                            (room_id, account))
            remaining = [p for p in room["players"] if p["accountId"] != account]
            if not remaining:
                self.db.execute("UPDATE rooms SET phase='cancelled',load_failure='所有成员已退出' WHERE id=?",
                                (room_id,))
            elif account == room["hostId"]:
                self.db.execute("UPDATE rooms SET host_id=?,phase='lobby',load_epoch='',load_failure='',server_allocation='' WHERE id=?",
                                (remaining[0]["accountId"], room_id))
                self.db.execute("UPDATE room_players SET ready=0,ready_epoch='' WHERE room_id=?", (room_id,))
            self._touch(room_id)
            return {"left": True}
