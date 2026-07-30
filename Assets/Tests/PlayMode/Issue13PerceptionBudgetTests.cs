using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue13PerceptionBudgetTests
    {
        [UnityTest]
        public IEnumerator PerceptionChecksRespectBudgetAndRotateMembers()
        {
            Type schedulerType = Type.GetType(
                "EnemyPerceptionScheduler, Assembly-CSharp");
            Type perceptionType = Type.GetType(
                "EnemyPerceptionController, Assembly-CSharp");
            Assert.That(
                schedulerType,
                Is.Not.Null,
                "Issue 13 需要提供固定预算的感知调度模块。");

            GameObject schedulerObject =
                new GameObject("Perception Scheduler Test");
            Component scheduler =
                schedulerObject.AddComponent(schedulerType);
            schedulerType.GetMethod("Configure")
                .Invoke(scheduler, new object[] { 2 });
            var enemies = new GameObject[5];

            try
            {
                for (int index = 0; index < enemies.Length; index++)
                {
                    enemies[index] =
                        new GameObject($"Budget Enemy {index + 1}");
                    Component perception =
                        enemies[index].AddComponent(perceptionType);
                    schedulerType.GetMethod("Register")
                        .Invoke(scheduler, new[] { perception });
                }

                for (int frame = 0; frame < 4; frame++)
                {
                    yield return null;
                    int checks = (int)schedulerType
                        .GetProperty("LastFrameCheckCount")
                        .GetValue(scheduler);
                    Assert.That(
                        checks,
                        Is.LessThanOrEqualTo(2),
                        "单帧视野检查不得突破固定预算。");
                }

                foreach (GameObject enemy in enemies)
                {
                    Component perception =
                        enemy.GetComponent(perceptionType);
                    int checks = (int)perceptionType
                        .GetProperty("SightCheckCount")
                        .GetValue(perception);
                    Assert.That(
                        checks,
                        Is.GreaterThan(0),
                        "分帧调度必须轮转，不能长期饿死后注册的敌人。");
                }
            }
            finally
            {
                foreach (GameObject enemy in enemies)
                {
                    if (enemy != null)
                    {
                        UnityEngine.Object.DestroyImmediate(enemy);
                    }
                }

                UnityEngine.Object.DestroyImmediate(schedulerObject);
            }
        }
    }
}
