#if UNITY_EDITOR

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools.DemoRecording
{
    /// <summary>
    /// Focus-independent showcase driver used only while capturing portfolio footage.
    /// It feeds the same PlayerInputSample abstraction used by deterministic replay, so
    /// the user's foreground keyboard and pointer are never intercepted.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class FpsDemoAutoplayDirector : MonoBehaviour
    {
        public const float DurationSeconds = 410f;

        public event Action<bool, string> Completed;

        private PlayerInputReader input;
        private PlayerController player;
        private PlayerCombatController combat;
        private PlayerInventoryController inventory;
        private PlayerUpgradeController upgrades;
        private CityNewMissionController mission;
        private Health playerHealth;
        private Transform cameraPitchPivot;
        private Camera gameplayCamera;
        private CombatRuntimeDiagnosticsPanel diagnostics;
        private RunReplayDebugTimeline replayTimeline;
        private EnemyController currentEnemy;
        private WorldItemPickup currentPickup;
        private Vector3 concreteAimPoint;
        private Vector3 metalAimPoint;
        private float startedAt;
        private float nextBindTime;
        private float nextKillTime;
        private float nextPickupTime;
        private float nextProgressLogTime;
        private bool concretePointResolved;
        private bool metalPointResolved;
        private bool jumpSent;
        private bool crouchSent;
        private bool standSent;
        private bool reloadSent;
        private bool handgunSent;
        private bool rifleSent;
        private bool lateCombatAmmoRefilled;
        private bool inventoryPrepared;
        private bool inventoryOpened;
        private bool inventoryClosed;
        private bool vitalsDamaged;
        private bool medicalUsed;
        private bool armorUsed;
        private bool upgradeQueued;
        private bool upgradeSelected;
        private bool saveMenuOpened;
        private bool snapshotSaved;
        private bool saveMenuClosed;
        private bool snapshotLoaded;
        private bool pickupWarped;
        private bool terminalWarped;
        private bool terminalStarted;
        private bool extractionWarped;
        private bool extractionCompleted;
        private bool firstDiagnosticsOpened;
        private bool firstDiagnosticsClosed;
        private bool finalDiagnosticsOpened;
        private bool replayOpened;
        private bool replayClosed;
        private bool finished;
        private int semiAutoBucket = -1;
        private GUIStyle captionStyle;
        private GUIStyle progressStyle;

        public float ElapsedSeconds => Mathf.Max(0f, Time.unscaledTime - startedAt);
        public string CurrentCaption => CaptionAt(ElapsedSeconds);

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            startedAt = Time.unscaledTime;
            nextProgressLogTime = 30f;
            SceneManager.sceneLoaded += OnSceneLoaded;
            BindRuntime(true);
            Debug.Log("[FPS Demo Autoplay] 自动演示时间轴已启动，时长 6 分 50 秒。");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            input?.StopReplay();
            SetDiagnosticsVisible(false);
            SetReplayTimelineVisible(false);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            input = null;
            player = null;
            combat = null;
            inventory = null;
            upgrades = null;
            mission = null;
            playerHealth = null;
            cameraPitchPivot = null;
            gameplayCamera = null;
            diagnostics = null;
            replayTimeline = null;
            nextBindTime = 0f;
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            Application.runInBackground = true;
            float elapsed = ElapsedSeconds;
            BindRuntime(false);

            if (elapsed >= DurationSeconds)
            {
                Finish(true, string.Empty);
                return;
            }

            if (input == null || player == null || gameplayCamera == null)
            {
                if (elapsed > 35f)
                {
                    Finish(false, "CityNew 玩家或主摄像机在 35 秒内没有完成初始化。");
                }
                return;
            }

            HandlePresentation(elapsed);
            HandleDirectActions(elapsed);
            AimForCurrentSection(elapsed);
            input.ApplyReplaySample(BuildInput(elapsed));
            KeepPlayerAlive(elapsed);

            if (elapsed >= nextProgressLogTime)
            {
                Debug.Log(
                    $"[FPS Demo Autoplay] {elapsed:F0}/{DurationSeconds:F0}s · {CurrentCaption}");
                nextProgressLogTime += 30f;
            }
        }

        private void BindRuntime(bool force)
        {
            if (!force && Time.unscaledTime < nextBindTime)
            {
                return;
            }

            nextBindTime = Time.unscaledTime + 0.5f;
            input ??= FindFirstObjectByType<PlayerInputReader>();

            if (input == null)
            {
                return;
            }

            player = input.GetComponent<PlayerController>();
            combat = input.GetComponent<PlayerCombatController>();
            inventory = input.GetComponent<PlayerInventoryController>();
            upgrades = input.GetComponent<PlayerUpgradeController>();
            mission = input.GetComponent<CityNewMissionController>();
            playerHealth = input.GetComponent<Health>();
            cameraPitchPivot = input.transform.Find("CameraPitchPivot");
            gameplayCamera = player != null ? player.AimCamera : Camera.main;
            gameplayCamera ??= Camera.main;
            diagnostics = input.GetComponent<CombatRuntimeDiagnosticsPanel>();
            replayTimeline = input.GetComponent<RunReplayDebugTimeline>();
        }

        private PlayerInputSample BuildInput(float elapsed)
        {
            Vector2 move = Vector2.zero;
            bool sprint = false;
            bool attackHeld = false;
            bool attackPressed = false;
            bool aimHeld = false;
            bool jump = false;
            bool crouch = false;
            bool reload = false;
            int weaponSlot = -1;

            if (elapsed >= 18f && elapsed < 24f)
            {
                move = Vector2.up;
            }
            else if (elapsed >= 24f && elapsed < 30f)
            {
                move = Vector2.up;
                sprint = true;
            }
            else if (elapsed >= 30f && elapsed < 36f)
            {
                move = Vector2.right;
            }
            else if (elapsed >= 36f && elapsed < 42f)
            {
                move = Vector2.left;
            }
            else if (elapsed >= 42f && elapsed < 45f)
            {
                move = Vector2.down * 0.55f;
            }

            if (!jumpSent && elapsed >= 27f)
            {
                jumpSent = true;
                jump = true;
            }

            if (!crouchSent && elapsed >= 37f)
            {
                crouchSent = true;
                crouch = true;
            }

            if (!standSent && elapsed >= 42f)
            {
                standSent = true;
                crouch = true;
            }

            if (elapsed >= 49f && elapsed < 53f)
            {
                attackHeld = true;
            }
            else if (elapsed >= 58f && elapsed < 60f)
            {
                aimHeld = true;
                attackHeld = true;
            }

            if (!reloadSent && elapsed >= 63.5f)
            {
                reloadSent = true;
                reload = true;
            }

            if (!handgunSent && elapsed >= 67.5f)
            {
                handgunSent = true;
                weaponSlot = 1;
            }

            if (elapsed >= 69.5f && elapsed < 72f)
            {
                int bucket = Mathf.FloorToInt((elapsed - 69.5f) / 0.55f);
                if (bucket != semiAutoBucket)
                {
                    semiAutoBucket = bucket;
                    attackPressed = true;
                }
            }

            if (!rifleSent && elapsed >= 72f)
            {
                rifleSent = true;
                weaponSlot = 0;
            }

            if ((elapsed >= 74f && elapsed < 76f) ||
                (elapsed >= 83f && elapsed < 85f))
            {
                attackHeld = true;
            }

            if (elapsed >= 96f && elapsed < 96.25f)
            {
                attackHeld = true;
            }

            if (elapsed >= 101f && elapsed < 111f)
            {
                move = new Vector2(-0.65f, -0.35f);
            }

            if (elapsed >= 154f && elapsed < 176f)
            {
                aimHeld = true;
                attackHeld = Mathf.Repeat(elapsed - 154f, 2.4f) < 0.45f;
                move = new Vector2(0.35f, 0.15f);
            }

            if (elapsed >= 294f && elapsed < 298.5f)
            {
                move = new Vector2(0.5f, 0.8f);
                sprint = true;
            }

            if (elapsed >= 334f && elapsed < 342f)
            {
                move = Vector2.up * 0.75f;
            }

            return new PlayerInputSample(
                move,
                Vector2.zero,
                false,
                jump,
                sprint,
                false,
                false,
                false,
                attackPressed,
                attackHeld,
                false,
                aimHeld,
                crouch,
                false,
                false,
                false,
                reload,
                weaponSlot,
                0,
                -1);
        }

        private void HandleDirectActions(float elapsed)
        {
            AutoResolveUnexpectedUpgrade(elapsed);

            if (!lateCombatAmmoRefilled && elapsed >= 152f &&
                combat?.EquippedWeapon != null)
            {
                lateCombatAmmoRefilled = combat.EquippedWeapon.TryRestoreAmmo(
                    combat.EquippedWeapon.MagazineCapacity,
                    combat.EquippedWeapon.MaximumReserveAmmo);
            }

            if (ShouldProgressWaves(elapsed) && elapsed >= nextKillTime)
            {
                KillOneEnemyForShowcase();
                nextKillTime = elapsed + (elapsed < 154f ? 5f : 1.15f);
            }

            if (!pickupWarped && elapsed >= 178f)
            {
                pickupWarped = true;
                currentPickup = FindClosestPickup();
                if (currentPickup != null)
                {
                    WarpNear(currentPickup.transform, 2.4f);
                }
            }

            if (elapsed >= 187f && elapsed < 201f && elapsed >= nextPickupTime)
            {
                currentPickup = FindClosestPickup();
                if (currentPickup != null && currentPickup.TryBegin(input.gameObject))
                {
                    currentPickup.Advance(input.gameObject, 0f);
                }
                nextPickupTime = elapsed + 3.5f;
            }

            if (!inventoryPrepared && elapsed >= 204f)
            {
                inventoryPrepared = true;
                AddShowcaseInventory();
            }

            if (!inventoryOpened && elapsed >= 206f && inventory != null)
            {
                inventoryOpened = inventory.Open();
            }

            if (!inventoryClosed && elapsed >= 231f && inventory != null)
            {
                inventoryClosed = true;
                inventory.Close();
            }

            if (!vitalsDamaged && elapsed >= 235f && playerHealth != null)
            {
                vitalsDamaged = true;
                playerHealth.ApplyDamage(new DamageInfo(
                    130f,
                    playerHealth.transform.position,
                    Vector3.back,
                    gameObject,
                    DamageType.Environment));
            }

            if (!medicalUsed && elapsed >= 241f && inventory != null)
            {
                medicalUsed = inventory.TryUseQuickSlot(0);
            }

            if (!armorUsed && elapsed >= 248f && inventory != null)
            {
                armorUsed = inventory.TryUseQuickSlot(1);
            }

            if (!upgradeQueued && elapsed >= 257f && upgrades != null)
            {
                upgradeQueued = true;
                upgrades.QueueUpgradeChoices(1);
            }

            if (!upgradeSelected && elapsed >= 275f && upgrades != null &&
                upgrades.IsChoiceOpen)
            {
                upgradeSelected = upgrades.TrySelect(0);
            }

            if (!saveMenuOpened && elapsed >= 284f && player != null)
            {
                saveMenuOpened = true;
                player.SetPaused(true);
            }

            if (!snapshotSaved && elapsed >= 287f && mission != null)
            {
                snapshotSaved = mission.SaveRunSnapshot();
            }

            if (!saveMenuClosed && elapsed >= 292f && mission != null)
            {
                saveMenuClosed = true;
                mission.ResumeGame();
            }

            if (!snapshotLoaded && elapsed >= 299f && mission != null)
            {
                input.StopReplay();
                snapshotLoaded = mission.LoadRunSnapshot();
            }

            if (!terminalWarped && elapsed >= 310f && mission?.Terminal != null)
            {
                terminalWarped = true;
                WarpNear(mission.Terminal.transform, 2.8f);
            }

            if (elapsed >= 312f && elapsed < 328f && mission?.Terminal != null &&
                mission.State == MissionFlowState.ActivateTerminal)
            {
                if (!terminalStarted)
                {
                    terminalStarted = mission.Terminal.TryBegin(input.gameObject);
                }

                if (terminalStarted &&
                    mission.Terminal.Advance(input.gameObject, Time.unscaledDeltaTime))
                {
                    terminalStarted = false;
                }
            }

            if (!extractionWarped && elapsed >= 332f &&
                mission?.ExtractionZone != null && mission.ExtractionAvailable)
            {
                extractionWarped = true;
                WarpNear(mission.ExtractionZone.transform, 7f);
            }

            if (!extractionCompleted && elapsed >= 342f && mission != null &&
                mission.ExtractionAvailable)
            {
                extractionCompleted = mission.TryEnterExtraction(input.gameObject);
            }
        }

        private void HandlePresentation(float elapsed)
        {
            if (!firstDiagnosticsOpened && elapsed >= 128f)
            {
                firstDiagnosticsOpened = true;
                SetDiagnosticsVisible(true);
            }

            if (!firstDiagnosticsClosed && elapsed >= 154f)
            {
                firstDiagnosticsClosed = true;
                SetDiagnosticsVisible(false);
            }

            if (!finalDiagnosticsOpened && elapsed >= 352f)
            {
                finalDiagnosticsOpened = true;
                SetDiagnosticsVisible(true);
            }

            if (!replayOpened && elapsed >= 388f)
            {
                replayOpened = true;
                SetDiagnosticsVisible(false);
                SetReplayTimelineVisible(true);
            }

            if (!replayClosed && elapsed >= 403f)
            {
                replayClosed = true;
                SetReplayTimelineVisible(false);
            }
        }

        private void AimForCurrentSection(float elapsed)
        {
            if (elapsed < 18f)
            {
                input.transform.Rotate(
                    Vector3.up,
                    5.5f * Time.unscaledDeltaTime,
                    Space.World);
                return;
            }

            if (elapsed >= 72f && elapsed < 83f)
            {
                AimAt(ResolveSurfacePoint(true));
                return;
            }

            if (elapsed >= 83f && elapsed < 95f)
            {
                AimAt(ResolveSurfacePoint(false));
                return;
            }

            if (elapsed >= 176f && elapsed < 204f)
            {
                currentPickup ??= FindClosestPickup();
                if (currentPickup != null)
                {
                    AimAt(currentPickup.transform.position + Vector3.up * 0.15f);
                }
                return;
            }

            if (elapsed >= 308f && elapsed < 330f && mission?.Terminal != null)
            {
                AimAt(mission.Terminal.transform.position + Vector3.up * 0.8f);
                return;
            }

            if (elapsed >= 330f && elapsed < 352f && mission?.ExtractionZone != null)
            {
                AimAt(mission.ExtractionZone.transform.position + Vector3.up * 0.8f);
                return;
            }

            if ((elapsed >= 45f && elapsed < 176f) ||
                (elapsed >= 298f && elapsed < 308f))
            {
                currentEnemy = FindClosestEnemy();
                if (currentEnemy != null)
                {
                    AimAt(GetPresentationCenter(currentEnemy.gameObject));
                }
            }
        }

        private bool ShouldProgressWaves(float elapsed)
        {
            if (mission == null || mission.State != MissionFlowState.EliminateTargets)
            {
                return false;
            }

            return (elapsed >= 50f && elapsed < 95f) ||
                   (elapsed >= 154f && elapsed < 176f) ||
                   (elapsed >= 202f && elapsed < 308f);
        }

        private void KillOneEnemyForShowcase()
        {
            EnemyController enemy = FindClosestEnemy();
            Health health = enemy != null ? enemy.GetComponent<Health>() : null;
            if (health == null || health.IsDead)
            {
                return;
            }

            Vector3 hitPoint = GetPresentationCenter(enemy.gameObject);
            Vector3 direction = gameplayCamera != null
                ? (hitPoint - gameplayCamera.transform.position).normalized
                : Vector3.forward;
            health.ApplyDamage(new DamageInfo(
                health.CurrentHealth + health.CurrentArmor + 1f,
                hitPoint,
                direction,
                input.gameObject,
                DamageType.Hitscan));
        }

        private void AutoResolveUnexpectedUpgrade(float elapsed)
        {
            if (upgrades == null || !upgrades.IsChoiceOpen)
            {
                return;
            }

            if (elapsed < 256f || elapsed >= 280f)
            {
                upgrades.TrySelect(0);
            }
        }

        private void AddShowcaseInventory()
        {
            if (inventory == null)
            {
                return;
            }

            AddItem("medical_kit", 4);
            AddItem("armor_pack", 4);
            AddItem("rifle_ammo", 6);
            AddItem("handgun_ammo", 6);
        }

        private void AddItem(string stableId, int quantity)
        {
            if (inventory.TryGetDefinition(stableId, out ItemDefinition definition))
            {
                inventory.TryAdd(definition, quantity);
            }
        }

        private void KeepPlayerAlive(float elapsed)
        {
            if (playerHealth == null ||
                (elapsed >= 234f && elapsed < 252f))
            {
                return;
            }

            if (playerHealth.IsDead ||
                playerHealth.CurrentHealth < 65f ||
                playerHealth.CurrentArmor < 25f)
            {
                playerHealth.TryRestoreSnapshotVitals(
                    playerHealth.MaxHealth,
                    playerHealth.MaxArmor);
            }
        }

        private EnemyController FindClosestEnemy()
        {
            EnemyController best = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 origin = gameplayCamera != null
                ? gameplayCamera.transform.position
                : input.transform.position;

            foreach (EnemyController candidate in
                     FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            {
                Health health = candidate.GetComponent<Health>();
                if (!candidate.gameObject.activeInHierarchy || health == null || health.IsDead)
                {
                    continue;
                }

                float distance = (candidate.transform.position - origin).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private WorldItemPickup FindClosestPickup()
        {
            WorldItemPickup best = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 origin = input != null ? input.transform.position : Vector3.zero;

            foreach (WorldItemPickup candidate in
                     FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None))
            {
                if (!candidate.gameObject.activeInHierarchy || candidate.IsClaimed)
                {
                    continue;
                }

                float distance = (candidate.transform.position - origin).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private Vector3 ResolveSurfacePoint(bool concrete)
        {
            if (concrete && concretePointResolved)
            {
                return concreteAimPoint;
            }
            if (!concrete && metalPointResolved)
            {
                return metalAimPoint;
            }

            string[] tokens = concrete
                ? new[] { "concrete", "wall", "pillar" }
                : new[] { "metal", "generator", "barrel", "tank" };
            Vector3 origin = gameplayCamera != null
                ? gameplayCamera.transform.position
                : input.transform.position;
            float bestDistance = float.PositiveInfinity;
            Vector3 bestPoint = origin + input.transform.forward * 12f;

            foreach (Renderer candidate in
                     FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                string candidateName = candidate.name.ToLowerInvariant();
                bool matches = Array.Exists(tokens, candidateName.Contains);
                if (!matches || !candidate.enabled)
                {
                    continue;
                }

                float distance = (candidate.bounds.center - origin).sqrMagnitude;
                if (distance < bestDistance && distance > 4f)
                {
                    bestDistance = distance;
                    bestPoint = candidate.bounds.center;
                }
            }

            if (concrete)
            {
                concretePointResolved = true;
                concreteAimPoint = bestPoint;
            }
            else
            {
                metalPointResolved = true;
                metalAimPoint = bestPoint;
            }

            return bestPoint;
        }

        private void AimAt(Vector3 worldPoint)
        {
            if (input == null || gameplayCamera == null)
            {
                return;
            }

            Vector3 direction = worldPoint - gameplayCamera.transform.position;
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude < 0.001f)
            {
                return;
            }

            input.transform.rotation = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
            if (cameraPitchPivot != null)
            {
                float pitch = -Mathf.Atan2(direction.y, horizontal.magnitude) * Mathf.Rad2Deg;
                cameraPitchPivot.localRotation = Quaternion.Euler(
                    Mathf.Clamp(pitch, -70f, 70f),
                    0f,
                    0f);
            }
        }

        private void WarpNear(Transform target, float distance)
        {
            if (target == null || player == null)
            {
                return;
            }

            Vector3 direction = Vector3.ProjectOnPlane(
                target.position - player.transform.position,
                Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            }
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.forward;
            }

            Vector3 position = target.position - direction * Mathf.Max(1f, distance);
            position.y = player.transform.position.y;
            player.TryRestoreSnapshotPose(
                position,
                Quaternion.LookRotation(direction, Vector3.up),
                0f,
                false,
                out _);
        }

        private static Vector3 GetPresentationCenter(GameObject target)
        {
            Renderer renderer = target.GetComponentInChildren<Renderer>();
            return renderer != null
                ? renderer.bounds.center
                : target.transform.position + Vector3.up * 0.5f;
        }

        private void SetDiagnosticsVisible(bool visible)
        {
            BindRuntime(true);
            diagnostics?.SetVisible(visible);
        }

        private void SetReplayTimelineVisible(bool visible)
        {
            BindRuntime(true);
            if (replayTimeline == null)
            {
                return;
            }

            MethodInfo method = typeof(RunReplayDebugTimeline).GetMethod(
                "SetVisible",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(replayTimeline, new object[] { visible });
        }

        private void Finish(bool succeeded, string details)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            input?.StopReplay();
            SetDiagnosticsVisible(false);
            SetReplayTimelineVisible(false);
            Completed?.Invoke(succeeded, details);
        }

        private void OnGUI()
        {
            if (finished)
            {
                return;
            }

            GUI.depth = -1000;
            EnsureStyles();
            string caption = CurrentCaption;
            Rect panel = new Rect(0f, Screen.height - 82f, Screen.width, 82f);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(60f, Screen.height - 68f, Screen.width - 120f, 42f),
                caption, captionStyle);
            GUI.Label(new Rect(Screen.width - 210f, Screen.height - 28f, 190f, 20f),
                $"{ElapsedSeconds / 60f:0.0} / {DurationSeconds / 60f:0.0} min",
                progressStyle);
            GUI.color = previous;

            float fade = FadeAlphaAt(ElapsedSeconds);
            if (fade > 0f)
            {
                GUI.depth = -2000;
                GUI.color = new Color(0f, 0f, 0f, fade);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture);
                GUI.color = previous;
            }
        }

        private void EnsureStyles()
        {
            captionStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 27,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            progressStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 13,
                normal = { textColor = new Color(0.65f, 0.9f, 0.88f) }
            };
        }

        private static float FadeAlphaAt(float elapsed)
        {
            return Mathf.Clamp01(
                FadeWindow(elapsed, 177f, 179f) +
                FadeWindow(elapsed, 298.5f, 301.5f) +
                FadeWindow(elapsed, 330.5f, 332.5f));
        }

        private static float FadeWindow(float elapsed, float start, float end)
        {
            if (elapsed < start || elapsed > end)
            {
                return 0f;
            }
            float normalized = Mathf.InverseLerp(start, end, elapsed);
            return Mathf.Sin(normalized * Mathf.PI);
        }

        private static string CaptionAt(float elapsed)
        {
            if (elapsed < 8f) return "《封锁区突围》｜Unity 6 单人肉鸽 FPS";
            if (elapsed < 18f) return "完整战局：清除威胁 → 强化构筑 → 接入终端 → 撤离结算";
            if (elapsed < 45f) return "第一人称移动：冲刺、跳跃与平滑下蹲";
            if (elapsed < 72f) return "武器闭环：ADS、后坐力、弹药、换弹与切枪";
            if (elapsed < 95f) return "命中反馈：材质差异、弹道、弹痕与部位伤害";
            if (elapsed < 128f) return "感知状态机：枪声调查、视野追击、遮挡搜索与巡逻恢复";
            if (elapsed < 154f) return "敌人职责：突袭、压制、支援与精英单位协同";
            if (elapsed < 176f) return "波次导演：威胁预算、定量生成与对象池复用";
            if (elapsed < 204f) return "掉落拾取：名称列表、滚轮选择与 F 键拾取";
            if (elapsed < 234f) return "消耗品背包：堆叠、拆分、拖拽交换与自动整理";
            if (elapsed < 256f) return "生存资源：生命、护甲与消耗品即时恢复";
            if (elapsed < 282f) return "肉鸽成长：经验升级、中文三选一与即时属性生效";
            if (elapsed < 308f) return "可靠存档：玩家位置、背包、成长、波次与敌人状态恢复";
            if (elapsed < 330f) return "任务链：清除威胁后接入终端";
            if (elapsed < 352f) return "战局闭环：终端完成后开放撤离并生成结算";
            if (elapsed < 372f) return "Utility AI：候选行为分数、选择原因与决策稳定机制";
            if (elapsed < 388f) return "运行时诊断：波次队列、对象池容量与复用状态";
            if (elapsed < 403f) return "确定性回放：按 Tick 定位输入、事件与状态快照";
            return "完整单局流程与工程验证均可复现｜github.com/zongjunlv/FPS";
        }
    }
}

#endif
