#!/usr/bin/env python3
"""Online, consistent SQLite backup and verification for FPS account data.

Usage:
  python3 fps_control_backup.py backup --database /srv/fps/data/fps-control.sqlite3 --output /srv/fps/data/backups/fps-YYYYMMDD.sqlite3
  python3 fps_control_backup.py verify --database /srv/fps/data/backups/fps-YYYYMMDD.sqlite3

For restore, stop fps-match-broker first, preserve the old database, then
install the verified backup at the configured database path. Never copy a
hot SQLite WAL database with cp or rsync.
"""

from __future__ import annotations

import argparse
from contextlib import closing
import os
import pathlib
import sqlite3
import tempfile

from fps_control_store import SCHEMA_VERSION


def verify(path: pathlib.Path) -> int:
    if not path.is_file():
        raise FileNotFoundError(path)
    uri = path.resolve().as_uri() + "?mode=ro"
    with closing(sqlite3.connect(uri, uri=True)) as db:
        integrity = db.execute("PRAGMA integrity_check").fetchone()[0]
        version = db.execute("PRAGMA user_version").fetchone()[0]
        if integrity != "ok" or version != SCHEMA_VERSION:
            raise RuntimeError(f"数据库校验失败：integrity={integrity}, schema={version}")
        for table in ("accounts", "access_tokens", "rooms", "room_players"):
            if db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?",
                          (table,)).fetchone() is None:
                raise RuntimeError(f"数据库缺少 {table} 表")
    return version


def backup(source: pathlib.Path, output: pathlib.Path) -> None:
    if not source.is_file() or source.resolve() == output.resolve():
        raise ValueError("源数据库不存在或备份路径与源相同")
    if output.exists():
        raise FileExistsError(f"备份文件已存在：{output}")
    output.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    fd, temporary = tempfile.mkstemp(prefix=".fps-backup-", suffix=".sqlite3",
                                     dir=output.parent)
    os.fchmod(fd, 0o600)
    os.close(fd)
    temporary_path = pathlib.Path(temporary)
    try:
        with closing(sqlite3.connect(source.resolve().as_uri() + "?mode=ro", uri=True)) as live:
            with closing(sqlite3.connect(temporary_path)) as destination:
                live.backup(destination)
        verify(temporary_path)
        if output.exists():
            raise FileExistsError(f"备份文件已存在：{output}")
        os.replace(temporary_path, output)
    finally:
        temporary_path.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("backup", "verify"))
    parser.add_argument("--database", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args()
    if args.command == "backup":
        if args.output is None:
            parser.error("backup 命令需要 --output")
        backup(args.database, args.output)
        print(f"备份完成并通过校验：{args.output}")
    else:
        version = verify(args.database)
        print(f"数据库校验通过，schemaVersion={version}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
