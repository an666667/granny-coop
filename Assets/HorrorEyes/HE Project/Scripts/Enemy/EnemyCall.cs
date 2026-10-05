using UnityEngine;

public class EnemyCall : MonoBehaviour {

    private GameObject[] enemy;

    private void Awake()
    {
        enemy = GameObject.FindGameObjectsWithTag("Enemy");
    }

    public void CallEnemy()
    {
        if (GrannyCoop.CoopSession.IsClient)
        {
            // the host runs the AI: send our noise to it instead of moving a frozen local enemy
            GrannyCoop.CoopSession.Instance.ReportNoise(transform.position);
            return;
        }
        for (int i = 0; i < enemy.Length; i++)
        {
            enemy[i].GetComponent<Enemy>().CallEnemy(transform.position);
        }
     
    }
}
