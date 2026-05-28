using UnityEngine;

public class GoalEffect : MonoBehaviour
{
    [SerializeField] private GameObject goalParticleObj;
    [SerializeField] private GameObject goalObj;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.gameObject.tag != "Ball")
        {
            return;
        }
        Vector3 colPos = collision.gameObject.transform.position;

        GameObject particleInstance = Instantiate(
            goalParticleObj,
            colPos,
            Quaternion.identity
        );

        ParticleSystem particle = particleInstance.GetComponent<ParticleSystem>();

        if (particle != null)
        {
            particle.Play();
        }
        else
        {
            Debug.Log("ParticleSystem not found");
        }
    }
}