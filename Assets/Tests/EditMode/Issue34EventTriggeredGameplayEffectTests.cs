using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue34EventTriggeredGameplayEffectTests
    {
        [Test]
        public void EventStreamCarriesStableContextAndSupportsUnsubscribe()
        {
            var source = new GameObject("Event Source");
            var target = new GameObject("Event Target");
            var stream = new GameplayEffectEventStream();
            int received = 0;
            GameplayEffectEventContext observed = default;
            System.Action<GameplayEffectEventContext> listener = context =>
            {
                received++;
                observed = context;
            };

            try
            {
                stream.Published += listener;
                stream.Publish(new GameplayEffectEventContext(
                    42,
                    GameplayEffectEventType.EnemyKilled,
                    "wave:1/spawn:7",
                    source,
                    target));

                Assert.That(received, Is.EqualTo(1));
                Assert.That(observed.EventId, Is.EqualTo(42));
                Assert.That(observed.SourceId, Is.EqualTo("wave:1/spawn:7"));
                Assert.That(observed.Source, Is.SameAs(source));
                Assert.That(observed.Target, Is.SameAs(target));

                stream.Published -= listener;
                stream.Publish(new GameplayEffectEventContext(
                    43,
                    GameplayEffectEventType.EnemyKilled,
                    "wave:1/spawn:8",
                    source,
                    target));
                Assert.That(received, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void MagazineGrantClampsWithoutTouchingReserve()
        {
            var ammo = new WeaponAmmoState(10, 30, 60);

            for (int index = 0; index < 7; index++)
            {
                Assert.That(ammo.TryConsumeRound(), Is.True);
            }

            Assert.That(ammo.CurrentAmmo, Is.EqualTo(3));
            Assert.That(ammo.AddMagazineAmmo(4), Is.EqualTo(4));
            Assert.That(ammo.CurrentAmmo, Is.EqualTo(7));
            Assert.That(ammo.ReserveAmmo, Is.EqualTo(30));
            Assert.That(ammo.AddMagazineAmmo(99), Is.EqualTo(3));
            Assert.That(ammo.CurrentAmmo, Is.EqualTo(10));
            Assert.That(ammo.AddMagazineAmmo(1), Is.Zero);
            Assert.That(ammo.ReserveAmmo, Is.EqualTo(30));
        }
    }
}
