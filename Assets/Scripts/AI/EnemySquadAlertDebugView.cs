using System.Collections.Generic;
using UnityEngine;

public sealed class EnemySquadAlertDebugView : MonoBehaviour
{
    private readonly List<LineRenderer> relationLines = new();
    private LineRenderer rangeLine;
    private Material lineMaterial;

    public bool IsActive { get; private set; }
    public float DisplayedRadius { get; private set; }

    public void Show(
        EnemySquadAlert alert,
        IReadOnlyList<EnemyAlertDebugRelation> relations)
    {
        EnsureMaterial();
        EnsureRangeLine();
        DisplayedRadius = alert.Radius;
        IsActive = true;
        DrawRange(alert.SourcePosition, alert.Radius);
        EnsureRelationLineCount(relations.Count);

        for (int index = 0; index < relationLines.Count; index++)
        {
            bool visible = index < relations.Count;
            LineRenderer line = relationLines[index];
            line.enabled = visible;

            if (!visible)
            {
                continue;
            }

            EnemyAlertDebugRelation relation = relations[index];
            Color color = relation.Accepted
                ? new Color(0.1f, 1f, 0.55f, 0.9f)
                : new Color(0.55f, 0.55f, 0.55f, 0.45f);
            line.startColor = color;
            line.endColor = color;
            line.positionCount = 3;
            line.SetPosition(
                0,
                alert.SourcePosition + Vector3.up * 0.65f);
            line.SetPosition(
                1,
                relation.ReceiverPosition + Vector3.up * 0.65f);
            line.SetPosition(
                2,
                relation.SearchPosition + Vector3.up * 0.12f);
        }
    }

    public void Hide()
    {
        IsActive = false;

        if (rangeLine != null)
        {
            rangeLine.enabled = false;
        }

        foreach (LineRenderer line in relationLines)
        {
            if (line != null)
            {
                line.enabled = false;
            }
        }
    }

    private void DrawRange(Vector3 center, float radius)
    {
        const int segmentCount = 64;
        rangeLine.enabled = true;
        rangeLine.positionCount = segmentCount + 1;

        for (int index = 0; index <= segmentCount; index++)
        {
            float angle = index * Mathf.PI * 2f / segmentCount;
            rangeLine.SetPosition(
                index,
                center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.08f,
                    Mathf.Sin(angle) * radius));
        }
    }

    private void EnsureRangeLine()
    {
        if (rangeLine != null)
        {
            return;
        }

        rangeLine = CreateLine("Alert Range", 0.055f);
        Color rangeColor = new Color(1f, 0.25f, 0.08f, 0.8f);
        rangeLine.startColor = rangeColor;
        rangeLine.endColor = rangeColor;
        rangeLine.loop = false;
    }

    private void EnsureRelationLineCount(int count)
    {
        while (relationLines.Count < count)
        {
            relationLines.Add(
                CreateLine(
                    $"Alert Relation {relationLines.Count + 1}",
                    0.07f));
        }
    }

    private LineRenderer CreateLine(string lineName, float width)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.material = lineMaterial;
        return line;
    }

    private void EnsureMaterial()
    {
        if (lineMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find(
            "Universal Render Pipeline/Unlit");
        shader ??= Shader.Find("Unlit/Color");

        if (shader != null)
        {
            lineMaterial = new Material(shader);
            lineMaterial.color = Color.white;
        }
    }

    private void OnDestroy()
    {
        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
        }
    }
}
