using System;
using System.Collections.Generic;
using UnityEngine;

public class PPPObstacleManager : MonoBehaviour
{
    [Header("Simulation Space")]
    [Tooltip("Projection area in Unity world units. Camera size should match this.")]
    [SerializeField] private Vector2 simulationWorldSize = new Vector2(16f, 9f);

    [Header("Obstacle")]
    [SerializeField] private float disappearGraceSeconds = 0.30f;
    [SerializeField] private bool createDebugSprite = true;
    [SerializeField] private Color debugColor = new Color(1f, 0.2f, 0.7f, 0.20f);
    [SerializeField] private Transform obstacleRoot;

    private readonly Dictionary<int, ObstacleEntry> entries = new Dictionary<int, ObstacleEntry>();
    private Sprite cachedSquareSprite;

    [Serializable]
    public class ObstacleDto
    {
        public int id;
        public float x;
        public float y;
        public float w;
        public float h;
        public float angle;
    }

    private class ObstacleEntry
    {
        public GameObject gameObject;
        public float lastSeenTime;
    }

    private void Awake()
    {
        if (obstacleRoot == null)
        {
            obstacleRoot = transform;
        }
    }

    public void ApplyFrame(ObstacleDto[] obstacles)
    {
        var seenIds = new HashSet<int>();

        if (obstacles != null)
        {
            foreach (var dto in obstacles)
            {
                if (dto == null) continue;
                seenIds.Add(dto.id);
                UpdateOrCreateObstacle(dto.id, dto.x, dto.y, dto.w, dto.h, dto.angle);
            }
        }

        CleanupOldEntries(seenIds);
    }

    private void UpdateOrCreateObstacle(int id, float normX, float normY, float normW, float normH, float angleDeg)
    {
        if (!entries.TryGetValue(id, out ObstacleEntry entry) || entry == null || entry.gameObject == null)
        {
            entry = CreateEntry(id);
            entries[id] = entry;
        }

        Vector2 worldPos = NormalizedToWorld(normX, normY);
        Vector2 worldSize = new Vector2(
            Mathf.Max(0.05f, normW * simulationWorldSize.x),
            Mathf.Max(0.05f, normH * simulationWorldSize.y)
        );

        entry.gameObject.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
        entry.gameObject.transform.rotation = Quaternion.Euler(0f, 0f, -angleDeg);
        entry.gameObject.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
        entry.lastSeenTime = Time.time;
    }

    private ObstacleEntry CreateEntry(int id)
    {
        var go = new GameObject($"PPP_Obstacle_{id}");
        go.transform.SetParent(obstacleRoot, false);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.simulated = true;

        var col = go.AddComponent<BoxCollider2D>();
        col.size = Vector2.one;
        col.isTrigger = false;

        if (createDebugSprite)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetSquareSprite();
            sr.color = debugColor;
            sr.sortingOrder = 10;
        }

        return new ObstacleEntry
        {
            gameObject = go,
            lastSeenTime = Time.time,
        };
    }

    private Vector2 NormalizedToWorld(float x, float y)
    {
        float worldX = (x - 0.5f) * simulationWorldSize.x;
        float worldY = (0.5f - y) * simulationWorldSize.y;
        return new Vector2(worldX, worldY);
    }

    private void CleanupOldEntries(HashSet<int> seenIds)
    {
        List<int> toRemove = null;

        foreach (var kv in entries)
        {
            bool seenNow = seenIds.Contains(kv.Key);
            bool expired = (Time.time - kv.Value.lastSeenTime) > disappearGraceSeconds;

            if (!seenNow && expired)
            {
                if (toRemove == null) toRemove = new List<int>();
                toRemove.Add(kv.Key);
            }
        }

        if (toRemove == null)
        {
            return;
        }

        foreach (int id in toRemove)
        {
            if (entries.TryGetValue(id, out ObstacleEntry entry) && entry != null && entry.gameObject != null)
            {
                Destroy(entry.gameObject);
            }

            entries.Remove(id);
        }
    }

    private Sprite GetSquareSprite()
    {
        if (cachedSquareSprite != null)
        {
            return cachedSquareSprite;
        }

        var tex = Texture2D.whiteTexture;
        cachedSquareSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        return cachedSquareSprite;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(simulationWorldSize.x, simulationWorldSize.y, 0f));
    }
#endif
}
