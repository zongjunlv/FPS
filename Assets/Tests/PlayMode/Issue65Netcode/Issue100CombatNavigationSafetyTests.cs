using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue100CombatNavigationSafetyTests
    {
        private readonly List<GameObject> created = new();
        private readonly List<NavMeshData> navigation = new();
        private readonly List<NavMeshDataInstance> instances = new();
        private readonly List<string> evidenceDirectories = new();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (NavMeshDataInstance instance in instances)
                if (instance.valid) instance.Remove();
            instances.Clear();
            foreach (NavMeshData data in navigation)
                if (data != null) Object.Destroy(data);
            navigation.Clear();
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null) Object.Destroy(created[index]);
            created.Clear();
            foreach (string directory in evidenceDirectories)
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            evidenceDirectories.Clear();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator PartialCombatRouteCannotFallBackThroughARealPhysicsObstacle()
        {
            var center = new Vector3(1000f, 0f, 1000f);
            BuildNavigation(new[]
            {
                BoxSource(center + Vector3.down * 0.25f, new Vector3(10f, 0.5f, 10f)),
                BoxSource(center + Vector3.right * 15f + Vector3.down * 0.25f,
                    new Vector3(10f, 0.5f, 10f))
            }, center + Vector3.right * 7.5f);
            Assert.That(NavMesh.SamplePosition(center, out NavMeshHit from, 1f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(center + Vector3.right * 15f,
                out NavMeshHit to, 1f, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathPartial),
                "必须使用真实断开的导航岛，让真实CalculatePath返回Partial，而非伪造path状态。");
            Assert.That(path.corners.Length, Is.GreaterThan(1));
            Vector3 endpoint = path.corners[path.corners.Length - 1];
            Vector3 towardTarget = to.position - endpoint;
            towardTarget.y = 0f;
            towardTarget.Normalize();
            Vector3 current = endpoint - towardTarget * 0.02f;
            var barrier = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            barrier.name = "Real low barrier beyond the partial route endpoint";
            barrier.transform.position = endpoint + towardTarget * 0.5f + Vector3.up * 0.3f;
            barrier.transform.localScale = new Vector3(0.2f, 0.6f, 8f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(CapsuleWouldHit(current, towardTarget, 0.75f), Is.True,
                "Partial末端的direct回退方向必须确实被真实Physics胶囊阻挡，不能只检查helper内部字段。");

            Component driver = CreateRealDriver(current, to.position);
            Vector3 direction = (Vector3)driver.GetType().GetMethod("ResolveNavigationDirection",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, new object[] { to.position });

            Assert.That(direction.sqrMagnitude <= 0.0001f ||
                !CapsuleWouldHit(current, direction.normalized, 0.75f), Is.True,
                "战斗导航不能把Partial当可达完整路线，再在尾端无条件direct撞进真实低障碍；须停下或选择物理可走的恢复方向。");
        }

        [UnityTest]
        public IEnumerator HistoricalCombatStallsCannotPermanentlyBlockARealSafeCompleteCorridor()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            Vector3 corridorCenter = new(current.x, -0.25f, current.z + 6f);
            Vector3 corridorSize = new(1.4f, 0.5f, 16f);
            CreateBox("Real safe complete corridor floor", corridorCenter, corridorSize);
            CreateBox("Real corridor left wall", current + Vector3.left * 0.8f +
                Vector3.up * 0.84f + Vector3.forward * 6f, new Vector3(0.2f, 2f, 16f));
            CreateBox("Real corridor right wall", current + Vector3.right * 0.8f +
                Vector3.up * 0.84f + Vector3.forward * 6f, new Vector3(0.2f, 2f, 16f));
            BuildNavigation(new[] { BoxSource(corridorCenter, corridorSize) },
                current + Vector3.forward * 6f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Vector3 target = current + Vector3.forward * 12f;
            Assert.That(NavMesh.SamplePosition(current, out NavMeshHit from, 1f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(target, out NavMeshHit to, 1f, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(path.corners.Length, Is.EqualTo(2),
                "复现真实矩阵完整二路点的直行通道，而不是用Partial或伪造NavMesh状态。");
            var collision = new CoopPlayerMovementCollision();
            Vector3 requested = Vector3.forward * 0.75f;
            Assert.That((collision.ResolveHorizontalDisplacement(current, requested) - requested).sqrMagnitude,
                Is.LessThan(0.0004f), "直行必须通过未放宽的正式共享胶囊guard。");
            foreach (float angle in new[] { -135f, -90f, -45f, 45f, 90f, 135f })
                Assert.That(CapsuleWouldHit(current, Quaternion.Euler(0f, angle, 0f) * Vector3.forward,
                    0.75f), Is.True, $"真实窄通道必须挡住side recovery角度{angle}，不能借另一条退路掩盖直行自锁。");

            Component driver = CreateRealDriver(current, target);
            MethodInfo resolve = driver.GetType().GetMethod("ResolveNavigationDirection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 fresh = (Vector3)resolve.Invoke(driver, new object[] { target });
            Assert.That(Vector3.Dot(fresh, Vector3.forward), Is.GreaterThan(0.99f),
                "同一完整可走路线在无历史stall时必须确实允许直行，作为真实绿对照。");

            // This is captured bot state, not a fake collider/path result: the
            // failed three-process run retained 66 historical stalls here.
            SetField(driver, "combatStallCount", 66);
            Vector3 afterHistoricalStalls = (Vector3)resolve.Invoke(driver, new object[] { target });
            Assert.That(Vector3.Dot(afterHistoricalStalls, Vector3.forward), Is.GreaterThan(0.99f),
                "过去66次停滞只能触发重规划/恢复，不能永久禁止当前已经通过真实Physics且PathComplete的安全直行。");
        }

        [UnityTest]
        public IEnumerator CombatLineIgnoresARealLegacyCharacterControllerButNotARealWorldWall()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            Vector3 target = current + Vector3.forward * 8f + Vector3.up * 1.25f;
            var actor = Track(new GameObject("Actual enabled legacy Player CharacterController"));
            actor.transform.position = current + Vector3.forward * 2f;
            CharacterController controller = actor.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.28f;
            controller.center = Vector3.up * 0.9f;
            Assert.That(controller.enabled, Is.True);
            Assert.That(controller.isTrigger, Is.False);
            Assert.That(actor.GetComponentInParent<NetworkPlayerReplica>(), Is.Null);
            Component driver = CreateRealDriver(current, target);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 origin = current + Vector3.up * 1.25f;
            Assert.That(Physics.Raycast(origin, Vector3.forward, out RaycastHit actorHit,
                7.95f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(actorHit.collider, Is.SameAs(controller),
                "必须真实命中与CityNew实际probe同型的enabled/nontrigger/noReplica Player CC。");
            MethodInfo resolve = driver.GetType().GetMethod("HasClearCombatLine",
                BindingFlags.Instance | BindingFlags.NonPublic);
            bool actorOnlyLine = (bool)resolve.Invoke(driver, new object[] { target });

            GameObject wall = CreateBox("Actual world wall must still block combat line",
                current + Vector3.forward * 4f + Vector3.up * 0.9f,
                new Vector3(3f, 1.8f, 0.3f));
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(Physics.RaycastAll(origin, Vector3.forward, 7.95f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider == wall.GetComponent<Collider>()), Is.True,
                "world wall对照也必须由真实Physics命中，不是伪造一个helper结果。");
            Assert.That((bool)resolve.Invoke(driver, new object[] { target }), Is.False,
                "统一actor过滤不能使真实世界墙可穿透或放宽伤害规则。");
            Assert.That(actorOnlyLine, Is.True,
                "真实legacy Player CC应与共享玩家胶囊/Jump过滤一致，不得成为bot永久禁止开火的假世界障碍。");
        }

        [UnityTest]
        public IEnumerator WaveAimAndMovementAreSampledEveryRenderFrameBeforeNextFireWindow()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            foreach (string methodName in new[] { "CompleteWave", "AssistWave" })
            {
                Component driver = CreateRealDriver(current, current + Vector3.forward * 8f, 90f);
                BindRealNavigationEvidence(driver);
                SetField(driver, "initialized", true);
                IEnumerator wave = (IEnumerator)driver.GetType().GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, null);
                try
                {
                    Assert.That(wave.MoveNext(), Is.True);
                    Assert.That(wave.Current, Is.InstanceOf<IEnumerator>());
                    var recovery = (IEnumerator)wave.Current;
                    Assert.That(recovery.MoveNext(), Is.False,
                        "真实Alive且没有倒地队友的RecoverTeam必须自然返回，不能mock跳过战斗恢复分支。");
                    Assert.That(wave.MoveNext(), Is.True);
                    var input = (NetworkVerticalSliceInputDriver)GetField(driver, "input");
                    Assert.That((float)GetField(input, "aimYaw"), Is.EqualTo(78f).Within(0.001f),
                        "必须确实执行真实AimAt的12°正常转向后才检查该帧yield。");
                    Assert.That(GetField(input, "fireQueued"), Is.False,
                        "初始90°偏角必须通过原3.5°开火门禁阻止射击，不准伪造RPC或提前伤害。");
                    Assert.That(wave.Current, Is.Null,
                        methodName + "应逐renderframe继续正常瞄准/安全移动；.18秒只是独立fire cadence，不能暂停所有瞄准采样。");
                }
                finally
                {
                    (wave as IDisposable)?.Dispose();
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HundredDegreeMovingAimTargetStarvesSparseSamplingButRealInputTickSamplingTracks()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            Vector3 floorCenter = current + Vector3.down * 0.41f;
            Vector3 floorSize = new(30f, 0.5f, 14f);
            CreateBox("Real aim-sampling floor", floorCenter, floorSize);
            BuildNavigation(new[] { BoxSource(floorCenter, floorSize) }, current);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(NavMesh.SamplePosition(current, out NavMeshHit from, 1f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(current + Vector3.forward * 2.4f,
                out NavMeshHit to, 1f, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));

            Component sparse = CreateRealDriver(current, current + Vector3.forward * 8f);
            int sparseFireCommands = 0;
            const double fireCadence = 0.18d;
            double timeout = (double)sparse.GetType().GetField("WaveClearTimeoutSeconds",
                BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            Assert.That(timeout, Is.EqualTo(95d));
            for (int sample = 0; sample * fireCadence < timeout; sample++)
            {
                double seconds = sample * fireCadence;
                long tick = (long)Math.Round(seconds * 60d) + 1;
                if (SendRealAimSample(sparse, MovingAimPoint(current, seconds), tick, fireDue: true))
                    sparseFireCommands++;
            }
            Assert.That(sparseFireCommands, Is.Zero,
                "真实旧采样证据：100°/s在±30°间往返、phase5°，95s每.18s才转12°且先判旧pose3.5°，始终不提交Fire。");

            Component continuous = CreateRealDriver(current, current + Vector3.forward * 8f);
            int continuousFireCommands = 0;
            double nextFireAt = 0d;
            for (long tick = 1; tick <= 180; tick++)
            {
                double seconds = (tick - 1) / 60d;
                bool fireDue = seconds + 0.0000001d >= nextFireAt;
                if (fireDue) nextFireAt += fireCadence;
                if (SendRealAimSample(continuous, MovingAimPoint(current, seconds), tick, fireDue))
                    continuousFireCommands++;
            }
            Assert.That(continuousFireCommands, Is.GreaterThan(8),
                "同一真实Driver/3.5°/12°/权威1080°规则，仅每输入Tick正常采样即可追上；Fire仍每.18s一次，不修改伤害或超时。");
        }

        [UnityTest]
        public IEnumerator MultipleRenderSamplesAtOneInputTickCannotAccumulateMoreThanTwelveAimDegrees()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            Vector3 target = current + Vector3.right * 8f + Vector3.up * 1.25f;
            Component driver = CreateRealDriver(current, target);
            var input = (NetworkVerticalSliceInputDriver)GetField(driver, "input");
            var replica = (NetworkPlayerReplica)GetField(driver, "replica");
            for (int frame = 0; frame < 12; frame++)
                driver.GetType().GetMethod("AimAt", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(driver, new object[] { target, false, false });
            Assert.That(input.ClientTick, Is.Zero);
            Assert.That((float)GetField(input, "aimYaw"), Is.EqualTo(12f).Within(0.001f),
                "高渲染频率同一真实输入Tick的多帧，不得把12°+12°累成一次24° wire turn。");
            SendRealInputFrame(driver, 1);
            Assert.That(replica.PresentedAimYaw, Is.EqualTo(12f).Within(0.001f));
            for (int frame = 0; frame < 12; frame++)
                driver.GetType().GetMethod("AimAt", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(driver, new object[] { target, false, false });
            Assert.That(input.ClientTick, Is.EqualTo(1));
            Assert.That((float)GetField(input, "aimYaw"), Is.EqualTo(24f).Within(0.001f));
            SendRealInputFrame(driver, 2);
            yield return null;
        }

        private static Vector3 MovingAimPoint(Vector3 current, double seconds)
        {
            // A bounded continuous aim trajectory, not a fake target state or
            // an invented network acknowledgement. Tangential speed at radius
            // 2.4 is 4.19m/s, below the formal Raider's 3.2 * 1.35m/s.
            double travel = (5d + seconds * 100d) % 120d;
            float yaw = (float)(travel < 60d ? -30d + travel : 90d - travel);
            return current + Vector3.up * 1.25f +
                Quaternion.Euler(0f, yaw, 0f) * (Vector3.forward * 2.4f);
        }

        private static bool SendRealAimSample(Component driver, Vector3 target, long tick, bool fireDue)
        {
            var authority = (NetworkCoopSessionAuthority)GetField(driver, "authority");
            while (authority.WorldState.ServerTick < tick - 1) authority.ServerStep();
            Assert.That((bool)driver.GetType().GetMethod("HasClearCombatLine",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, new object[] { target }), Is.True,
                "瞄准采样回归必须由实际Physics证明LOS清晰，不可用假CanFire或世界墙解释停火。");
            bool aligned = (bool)driver.GetType().GetMethod("CanFireAt",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, new object[] { target, 3.5f });
            driver.GetType().GetMethod("AimAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(driver, new object[] { target, fireDue && aligned, false });
            return SendRealInputFrame(driver, tick);
        }

        private static bool SendRealInputFrame(Component driver, long tick)
        {
            var input = (NetworkVerticalSliceInputDriver)GetField(driver, "input");
            var replica = (NetworkPlayerReplica)GetField(driver, "replica");
            var authority = (NetworkCoopSessionAuthority)GetField(driver, "authority");
            bool fire = (bool)GetField(input, "fireQueued");
            SetField(input, "fireQueued", false);
            NetcodePlayerCommand command = replica.BuildPredictedCommand(0f, 0f,
                (float)GetField(input, "aimYaw"), (float)GetField(input, "aimPitch"), fire, tick,
                jumpPressed: false, sprintHeld: false, crouchRequested: false, aimingHeld: true);
            Assert.That(authority.TryQueueCommand(10UL, command, false), Is.True);
            CommandResolution result = authority.ServerStep().Commands.Single();
            Assert.That(result.Accepted, Is.True,
                $"真实权威规则必须接受每tick≤12°转向：tick={tick};reject={result.RejectionReason}");
            Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
            replica.ConsumeServerState(state, true, state.ServerTick);
            SetField(input, "clientTick", tick);
            return fire;
        }

        [UnityTest]
        public IEnumerator MoveToUsesRealJumpInputToCrossACompletePathOverACityNewSizedLowWall()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            Vector3 floorCenter = new(current.x + 5f, -0.25f, current.z);
            Vector3 floorSize = new(30f, 0.5f, 14f);
            Vector3 wallCenter = new(current.x + 0.41f, 0.5333f * 0.5f, current.z);
            Vector3 wallSize = new(0.21f, 0.5333f, 8f);
            CreateBox("Real MoveTo landing floor", floorCenter, floorSize);
            CreateBox("Real CityNew wall.fbx-sized MoveTo low wall", wallCenter, wallSize);
            Assert.That(NavMesh.GetSettingsByIndex(0).agentClimb, Is.GreaterThan(wallSize.y),
                "正式agentClimb可爬的低墙，与没有step-up的权威胶囊规则形成实际差异。");
            BuildNavigation(new[] { BoxSource(floorCenter, floorSize), BoxSource(wallCenter, wallSize) },
                current + Vector3.right * 5f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 target = current + Vector3.right * 8f;
            Assert.That(NavMesh.SamplePosition(current, out NavMeshHit from, 1f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(target, out NavMeshHit to, 1f, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(path.corners.Length, Is.GreaterThan(1));
            var collision = new CoopPlayerMovementCollision();
            Assert.That(collision.ResolveHorizontalDisplacement(current, Vector3.right * 0.1f).sqrMagnitude,
                Is.LessThan(0.0001f), "复现停点：完整导航路线的第一权威平面胶囊步已被真实低墙合法阻挡。");

            Component driver = CreateRealDriver(current, target, 90f);
            BindRealNavigationEvidence(driver);
            Assert.That((bool)driver.GetType().GetMethod("CanJumpCombatObstacle",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver,
                new object[] { current, Vector3.right }), Is.True,
                "现有grounded/full-apex/crossing/真实landing保护必须全部允许；此修复不能修改Jump安全参数。");
            IEnumerator moveTo = (IEnumerator)driver.GetType().GetMethod("MoveTo",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver,
                new object[] { target, 0.45f, 20d });
            try
            {
                Assert.That(moveTo.MoveNext(), Is.True);
                var input = (NetworkVerticalSliceInputDriver)GetField(driver, "input");
                Assert.That(GetField(input, "jumpQueued"), Is.True,
                    "真实MoveTo须把已确认合法的低墙跳跃交给正式exclusive input口，不能永远朝完整NavMesh路点挤平面胶囊。");
                var replica = (NetworkPlayerReplica)GetField(driver, "replica");
                var authority = (NetworkCoopSessionAuthority)GetField(driver, "authority");
                Vector2 movement = (Vector2)GetField(input, "movement");
                float yaw = (float)GetField(input, "aimYaw");
                float pitch = (float)GetField(input, "aimPitch");
                for (long tick = 2; tick <= 64; tick++)
                {
                    NetcodePlayerCommand command = replica.BuildPredictedCommand(
                        movement.x, movement.y, yaw, pitch, false, tick,
                        jumpPressed: tick == 2, sprintHeld: (bool)GetField(input, "sprintHeld"),
                        crouchRequested: false, aimingHeld: false);
                    Assert.That(authority.TryQueueCommand(10UL, command, false), Is.True);
                    CommandResolution result = authority.ServerStep().Commands.Single();
                    Assert.That(result.Accepted, Is.True, $"tick={tick};reject={result.RejectionReason}");
                    Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
                    if (tick == 2)
                    {
                        Assert.That(command.JumpPressed, Is.True);
                        Assert.That(state.Position.y, Is.GreaterThan(current.y));
                        Assert.That(state.Grounded, Is.False);
                    }
                    replica.ConsumeServerState(state, true, state.ServerTick);
                }
                Assert.That(authority.LastAuthoritativeSnapshot.Player(1).Position.X,
                    Is.GreaterThan(current.x + 1.6f),
                    "MoveTo的正式Jump输入必须自然跨过真实低墙，不准teleport、step-up或放宽超时/radius。");
            }
            finally
            {
                (moveTo as IDisposable)?.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator LowCombatBarrierUsesRealJumpInputAndNaturallyCrossesOnAuthority()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            BuildPhysicalJumpFixture(current, ceiling: false, landing: true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(CapsuleWouldHit(current, Vector3.right, 0.75f), Is.True);
            Vector3 target = current + Vector3.right * 12f + Vector3.up * 1.25f;
            Component driver = CreateRealDriver(current, target, 90f);
            InvokeAimAt(driver, target);
            var replica = (NetworkPlayerReplica)GetField(driver, "replica");
            var authority = (NetworkCoopSessionAuthority)GetField(driver, "authority");
            var input = (NetworkVerticalSliceInputDriver)GetField(driver, "input");
            Assert.That(GetField(input, "jumpQueued"), Is.True,
                "必须由实际AimAt写入真实exclusive input口，不能只伪造导航helper的jump bool。");
            Vector2 movement = (Vector2)GetField(input, "movement");
            float yaw = (float)GetField(input, "aimYaw");
            float pitch = (float)GetField(input, "aimPitch");
            // Offline fixture cannot call an NGO RPC. Build the actual wire
            // command from the real input port, then run the real authority;
            // subsequent ticks consume the one-shot JumpPressed normally.
            for (long tick = 2; tick <= 64; tick++)
            {
                NetcodePlayerCommand command = replica.BuildPredictedCommand(
                    movement.x, movement.y, yaw, pitch, false, tick,
                    jumpPressed: tick == 2, sprintHeld: true,
                    crouchRequested: false, aimingHeld: true);
                Assert.That(authority.TryQueueCommand(10UL, command, false), Is.True);
                CommandResolution result = authority.ServerStep().Commands.Single();
                Assert.That(result.Accepted, Is.True, $"tick={tick};reject={result.RejectionReason}");
                Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
                if (tick == 2)
                {
                    Assert.That(command.JumpPressed, Is.True);
                    Assert.That(state.Position.y, Is.GreaterThan(current.y));
                    Assert.That(state.Grounded, Is.False);
                }
                replica.ConsumeServerState(state, true, state.ServerTick);
            }
            Assert.That(authority.LastAuthoritativeSnapshot.Player(1).Position.X,
                Is.GreaterThan(current.x + 1.6f),
                "须通过真实Jump与服务器胶囊自然越障，不准teleport或只检查请求bool。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LowCombatBarrierDoesNotJumpIntoFullApexCeiling()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            BuildPhysicalJumpFixture(current, ceiling: true, landing: true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 target = current + Vector3.right * 12f + Vector3.up * 1.25f;
            Component driver = CreateRealDriver(current, target, 90f);
            InvokeAimAt(driver, target);
            Assert.That(GetField(GetField(driver, "input"), "jumpQueued"), Is.False,
                "站立空间足够但完整跳跃apex头顶受阻时不能请求Jump；服务器不会替bot做垂直碰撞step-up。");
        }

        [UnityTest]
        public IEnumerator LowCombatBarrierDoesNotJumpWithoutPhysicalLanding()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            BuildPhysicalJumpFixture(current, ceiling: false, landing: false);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 target = current + Vector3.right * 12f + Vector3.up * 1.25f;
            Component driver = CreateRealDriver(current, target, 90f);
            InvokeAimAt(driver, target);
            Assert.That(GetField(GetField(driver, "input"), "jumpQueued"), Is.False,
                "离散导航岛或实际无地面时不能以Jump绕过落脚验证。");
        }

        [UnityTest]
        public IEnumerator AirborneCombatReplicaDoesNotQueueAnotherObstacleJump()
        {
            Vector3 current = new(2000f, 0.16f, 2000f);
            BuildPhysicalJumpFixture(current, ceiling: false, landing: true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 target = current + Vector3.right * 12f + Vector3.up * 1.25f;
            Component driver = CreateRealDriver(current, target, 90f, initiallyJumping: true);
            var replica = (NetworkPlayerReplica)GetField(driver, "replica");
            Assert.That(replica.PresentedGrounded, Is.False,
                "空中状态必须来自真实已接受Jump的authority snapshot，不能反射伪造Grounded。");
            InvokeAimAt(driver, target);
            Assert.That(GetField(GetField(driver, "input"), "jumpQueued"), Is.False);
        }

        private void BuildPhysicalJumpFixture(Vector3 current, bool ceiling, bool landing)
        {
            if (landing)
                CreateBox("Real combat landing floor", new Vector3(current.x + 3f, -0.25f, current.z),
                    new Vector3(20f, 0.5f, 20f));
            CreateBox("Real CityNew-sized low combat barrier",
                new Vector3(current.x + 0.7f, 0.245f, current.z), new Vector3(0.2f, 0.49f, 8f));
            if (ceiling)
                CreateBox("Ceiling above standing but below actual jump apex",
                    current + Vector3.up * 2.7f + Vector3.right * 0.7f,
                    new Vector3(5f, 0.2f, 5f));
        }

        private GameObject CreateBox(string name, Vector3 position, Vector3 size)
        {
            var value = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            value.name = name;
            value.transform.position = position;
            value.transform.localScale = size;
            return value;
        }

        private Component CreateRealDriver(Vector3 current, Vector3 target,
            float initialYaw = 0f, bool initiallyJumping = false)
        {
            var authority = Track(new GameObject("Combat navigation real authority"))
                .AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(tickRate: 60),
                new[] { new CoopPlayerSpawn(1, ToDomain(current)) },
                new[] { new CoopTargetSpawn(12, ToDomain(target), 0.5d, 100d,
                    role: AuthoritativeEnemyRole.Suppressor, moveSpeed: 0d, attackDamage: 0d) });
            authority.RegisterPlayerClient(10UL, 1);
            if (initialYaw != 0f || initiallyJumping)
            {
                NetcodePlayerCommand initial = NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                    1, 1, 101, 1, 0d, 0d, initialYaw, 0d, false, ToDomain(current),
                    initiallyJumping, false, false));
                Assert.That(authority.TryQueueCommand(10UL, initial, false), Is.True);
                Assert.That(authority.ServerStep().Commands.Single().Accepted, Is.True);
            }
            var replica = Track(new GameObject("Combat navigation real replica"))
                .AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
            replica.ConsumeServerState(state, true, state.ServerTick);
            if (!initiallyJumping)
                Assert.That(Vector3.Distance(replica.PresentedPosition, current), Is.LessThan(0.0001f));
            Type driverType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("FPS.Networking.Acceptance.Issue100ClientScenarioDriver"))
                .Single(value => value != null);
            Component driver = Track(new GameObject("Actual Issue100 combat navigation driver"))
                .AddComponent(driverType);
            SetField(driver, "replica", replica);
            SetField(driver, "authority", authority);
            NetworkVerticalSliceInputDriver input = replica.gameObject.AddComponent<NetworkVerticalSliceInputDriver>();
            input.enabled = false;
            Assert.That(input.TryAcquireExclusiveInput(driver), Is.True);
            SetField(driver, "input", input);
            return driver;
        }

        private static void InvokeAimAt(Component driver, Vector3 target) =>
            driver.GetType().GetMethod("AimAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(driver, new object[] { target, false, true });

        private void BindRealNavigationEvidence(Component driver)
        {
            string directory = Path.Combine(Path.GetTempPath(), "issue100-moveto-nav-" + Guid.NewGuid().ToString("N"));
            evidenceDirectories.Add(directory);
            Assembly assembly = driver.GetType().Assembly;
            Type optionsType = assembly.GetType("FPS.Networking.Acceptance.Issue100RuntimeArguments");
            object[] arguments =
            {
                new[] { "-issue100-acceptance", "-issue100-role", "client-a",
                    "-issue100-scenario", "rtt-000-loss-00", "-issue100-run-id", "moveto-nav-fixture",
                    "-issue100-output", directory, "-issue99-match", "moveto-nav-match",
                    "-issue86-account", "moveto-nav-account" }, null, null
            };
            Assert.That((bool)optionsType.GetMethod("TryParse").Invoke(null, arguments), Is.True,
                arguments[2]?.ToString());
            SetField(driver, "options", arguments[1]);
            Type evidenceType = assembly.GetType("FPS.Networking.Acceptance.Issue100EvidenceStore");
            SetField(driver, "evidence", Activator.CreateInstance(evidenceType, new[] { arguments[1] }));
        }

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static bool CapsuleWouldHit(Vector3 current, Vector3 direction, float distance) =>
            Physics.CapsuleCast(current + Vector3.up * (0.28f + 0.03f),
                current + Vector3.up * (1.8f - 0.28f - 0.03f), 0.28f,
                direction, out _, distance + 0.03f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

        private void BuildNavigation(IEnumerable<NavMeshBuildSource> sources, Vector3 center)
        {
            Assert.That(NavMesh.GetSettingsCount(), Is.GreaterThan(0));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0),
                sources.ToList(), new Bounds(center, new Vector3(40f, 10f, 16f)),
                Vector3.zero, Quaternion.identity);
            Assert.That(data, Is.Not.Null);
            navigation.Add(data);
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            Assert.That(instance.valid, Is.True);
            instances.Add(instance);
        }

        private static NavMeshBuildSource BoxSource(Vector3 center, Vector3 size) => new()
        {
            shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
            size = size,
            area = 0
        };

        private static NetVector3 ToDomain(Vector3 value) => new(value.x, value.y, value.z);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }
    }
}
