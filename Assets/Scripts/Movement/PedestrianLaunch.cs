using UnityEngine;

/// <summary>
/// Projette un piéton en l'air (trajectoire balistique + culbute + un rebond), en temps non mis à l'échelle.
/// Ajouté par le GameManager sur la victime au moment de l'accident, retiré avant le rembobinage.
/// </summary>
public class PedestrianLaunch : MonoBehaviour
{
    const float Gravity = 9.81f;

    Vector3 vel, spinAxis;
    float spinSpeed, groundY, settleT;
    int bounces;
    bool settled;
    Quaternion lyingRot, settleFrom;

    public void Begin(Vector3 direction, float horizontalSpeed, float upSpeed, float spin)
    {
        vel = direction * horizontalSpeed + Vector3.up * upSpeed;
        groundY = transform.position.y;
        spinAxis = Vector3.Cross(Vector3.up, direction);   // culbute vers l'avant
        spinSpeed = spin;
        lyingRot = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);   // allongé sur le dos

        // les bras et jambes s'agitent pendant le vol (Animator en temps réel, car le jeu est en pause)
        foreach (var a in GetComponentsInChildren<Animator>(true))
        {
            a.updateMode = AnimatorUpdateMode.UnscaledTime;
            foreach (var prm in a.parameters)
            {
                if (prm.type != AnimatorControllerParameterType.Bool) continue;
                if (prm.name == "Running") a.SetBool(prm.nameHash, true);
                else if (prm.name == "Walking") a.SetBool(prm.nameHash, false);
            }
            a.speed = 1.5f;
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (settled)
        {
            // se pose doucement à plat
            settleT = Mathf.Min(1f, settleT + dt / 0.3f);
            transform.rotation = Quaternion.Slerp(settleFrom, lyingRot, settleT);
            return;
        }

        vel += Vector3.down * Gravity * dt;
        transform.position += vel * dt;
        transform.Rotate(spinAxis, spinSpeed * dt, Space.World);

        if (transform.position.y <= groundY && vel.y < 0f)
        {
            Vector3 p = transform.position;
            p.y = groundY;
            transform.position = p;

            bounces++;
            if (bounces >= 2 || -vel.y < 2f)
            {
                settled = true;
                settleFrom = transform.rotation;
                foreach (var a in GetComponentsInChildren<Animator>(true)) a.speed = 0f;
            }
            else
            {
                vel = new Vector3(vel.x * 0.5f, -vel.y * 0.35f, vel.z * 0.5f);
                spinSpeed *= 0.5f;
            }
        }
    }
}