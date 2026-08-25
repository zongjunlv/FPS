using System;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public enum GameplayEffectEventType
    {
        EnemyKilled
    }

    public readonly struct GameplayEffectEventContext
    {
        public GameplayEffectEventContext(
            long eventId,
            GameplayEffectEventType eventType,
            string sourceId,
            UnityEngine.Object source,
            UnityEngine.Object target)
        {
            EventId = eventId;
            EventType = eventType;
            SourceId = sourceId ?? string.Empty;
            Source = source;
            Target = target;
        }

        public long EventId { get; }
        public GameplayEffectEventType EventType { get; }
        public string SourceId { get; }
        public UnityEngine.Object Source { get; }
        public UnityEngine.Object Target { get; }
    }

    public sealed class GameplayEffectEventStream
    {
        public event Action<GameplayEffectEventContext> Published;

        public int PublishCount { get; private set; }
        public GameplayEffectEventContext LastPublished { get; private set; }

        public void Publish(GameplayEffectEventContext context)
        {
            LastPublished = context;
            PublishCount++;
            Published?.Invoke(context);
        }
    }
}
