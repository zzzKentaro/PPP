using UnityEngine;

public class PPPBallSpawner : MonoBehaviour
{
    [SerializeField] private GameObject spawnPoint;
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private float spawnInterval = 0.6f;
    [SerializeField] private Vector2 spawnXRange = new Vector2(-6.5f, 6.5f);
    [SerializeField] private float spawnY = 4.4f;
    [SerializeField] private Vector2 initialVelocity = new Vector2(0f, 0f);
    [SerializeField] private bool randomizeXVelocity = false;
    [SerializeField] private float randomXVelocity = 1.2f;

    private float nextSpawnTime;

    private void Update()
    {
        if (ballPrefab == null || Time.time < nextSpawnTime)
        {
            return;
        }

        nextSpawnTime = Time.time + spawnInterval;
        SpawnOne();
    }

    private void SpawnOne()
    {
        float x = Random.Range(spawnXRange.x, spawnXRange.y);
        GameObject ball = Instantiate(ballPrefab, spawnPoint.transform.position, Quaternion.identity);

        var rb = ball.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            Vector2 velocity = initialVelocity;
            if (randomizeXVelocity)
            {
                velocity.x += Random.Range(-randomXVelocity, randomXVelocity);
            }
            rb.linearVelocity = velocity;
        }
    }
}
