using UnityEngine;

namespace Hearthvale
{
    /// <summary>One Stinker. Holds its state; CritterSystem decides what it does. Bullets reach it through IShootable.</summary>
    public sealed class Critter : MonoBehaviour, IShootable
    {
        public int id, type; public CritterType t; public AnimalRig rig; public CritterSystem system; public CharacterController cc;
        public float hp, walk, moving, wander, fartCd, ambientCd, fartT, flash, wobble, stagger, detourT, stuck;
        public float yaw, targetYaw;                 // Unity yaw (radians): front = +z, rear = -z
        public Vector3 knock, lean, detourDir; public bool ambient, chasing; public float velY;
        public bool dying;

        public void OnShot(float damage, Vector3 hitPoint, Vector3 dir, float force)
        {
            if (dying) return;
            system.Hit(this, damage, dir, force, hitPoint);
        }

        // realistic gait: diagonal legs move together, body rolls and bobs, head nods, tail sways; farting = tail up and a heave
        void AnimateQuad(float dt)
        {
            yaw = Mathf.LerpAngle(yaw * Mathf.Rad2Deg, targetYaw * Mathf.Rad2Deg, Mathf.Min(1, dt * (fartT > 0 ? 14 : 6))) * Mathf.Deg2Rad;
            transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            float speed = moving; walk += dt * (2.2f + speed * 1.5f);
            float amp = speed > 0.1f ? Mathf.Clamp01(speed / 4f) * (type == 1 ? 38f : 30f) : 0f, sw = Mathf.Sin(walk);
            // legs: FL+BR together, FR+BL together (pairs 0,3 and 1,2); lower legs fold as they lift
            for (int i = 0; i < 4; i++)
            {
                bool a = i == 0 || i == 3; float ph = a ? sw : -sw; var up = rig.bones[Quadruped.FL + i * 2]; var lo = rig.bones[Quadruped.FL + i * 2 + 1];
                float swing = ph * amp; up.localRotation = Quaternion.Euler(-swing, 0, 0);
                lo.localRotation = Quaternion.Euler(Mathf.Max(0, -(a ? Mathf.Cos(walk) : -Mathf.Cos(walk))) * amp * 0.9f + (i < 2 ? 4f : -4f), 0, 0);
            }
            float f = fartT > 0 ? Mathf.Sin(Mathf.Min(1, (0.9f - fartT) / 0.9f) * Mathf.PI) : 0;
            wobble = Mathf.Max(0, wobble - dt * 3); float wob = Mathf.Sin(wobble * 25) * wobble;
            Vector3 ll = Quaternion.Inverse(transform.rotation) * lean;
            float breathe = Mathf.Sin(Time.time * 1.6f + id) * 0.008f;
            rig.body.localPosition = rig.restPos + new Vector3(0, (speed > 0.1f ? Mathf.Abs(Mathf.Sin(walk)) * 0.035f : 0f) + breathe - f * 0.02f, 0);
            rig.body.localRotation = Quaternion.Euler((ll.z * wobble * 10f) + f * 8f, sw * amp * 0.12f, (-ll.x * wobble * 10f) + sw * amp * 0.1f + wob * 3f);
            rig.bones[Quadruped.Chest].localRotation = Quaternion.Euler(-Mathf.Abs(sw) * amp * 0.06f, -sw * amp * 0.12f, 0);
            rig.bones[Quadruped.Neck].localRotation = Quaternion.Euler(Mathf.Sin(walk * 2) * amp * 0.06f + f * -10f, f * 28f, 0);
            rig.head.localRotation = Quaternion.Euler(Mathf.Sin(walk * 2 + 0.6f) * amp * 0.08f + f * 6f, f * 18f, 0); // looks back over its shoulder, very pleased with itself
            float tailUp = -f * 55f + Mathf.Sin(walk * 1.0f) * (speed > 0.1f ? 6f : 2f);
            rig.bones[Quadruped.Tail0].localRotation = Quaternion.Euler(tailUp, Mathf.Sin(walk * 1.3f) * (type == 1 ? 14f : 8f), 0);
            rig.bones[Quadruped.Tail1].localRotation = Quaternion.Euler(tailUp * 0.5f, Mathf.Sin(walk * 1.3f - 0.8f) * (type == 1 ? 18f : 8f), 0);
            rig.bones[Quadruped.Tail2].localRotation = Quaternion.Euler(0, Mathf.Sin(walk * 1.3f - 1.6f) * (type == 1 ? 22f : 8f), 0);
            flash = Mathf.Max(0, flash - dt); AnimalModels.SetFlash(rig, flash > 0 ? 0.6f : 0f);
        }

        // ------------------------------------------------------------------ visuals (port of Critters.animate)
        public void Animate(float dt, Camera cam)
        {
            if (rig.quad) { AnimateQuad(dt); return; }
            yaw = Mathf.LerpAngle(yaw * Mathf.Rad2Deg, targetYaw * Mathf.Rad2Deg, Mathf.Min(1, dt * (fartT > 0 ? 14 : 8))) * Mathf.Deg2Rad;
            float speed = moving;
            walk += dt * (3 + speed * 2.2f);
            float swing = speed > 0.1f ? Mathf.Sin(walk) * 0.7f : 0;
            for (int i = 0; i < rig.legs.Count; i++) rig.legs[i].localRotation = Quaternion.Euler(-(i == 1 ? swing : -swing) * Mathf.Rad2Deg, 0, 0);
            for (int i = 0; i < rig.arms.Count; i++)
            {
                float x = (i == 1 ? -swing : swing) * 0.8f + (fartT > 0 ? -0.6f : 0);
                rig.arms[i].localRotation = Quaternion.Euler(-x * Mathf.Rad2Deg, 0, Mathf.Sign(rig.arms[i].localPosition.x) * 0.25f * Mathf.Rad2Deg);
            }
            float bounce = speed > 0.1f ? Mathf.Abs(Mathf.Sin(walk)) * 0.06f : 0;
            transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            rig.body.localPosition = new Vector3(0, bounce, 0);
            // farting pose: bum up, tail raised, a big squash right when it lets go
            float f = fartT > 0 ? Mathf.Sin(Mathf.Min(1, (0.9f - fartT) / 0.9f) * Mathf.PI) : 0;
            float tailX = -f * 1.1f + (speed > 0.1f ? Mathf.Sin(walk * 2) * 0.15f : Mathf.Sin(walk * 0.5f) * 0.08f);
            rig.tail.localRotation = Quaternion.Euler(-tailX * Mathf.Rad2Deg, 0, 0);
            wobble = Mathf.Max(0, wobble - dt * 3);
            float wob = Mathf.Sin(wobble * 25) * wobble * 0.3f;
            float squish = 1 + f * 0.12f + wob;
            rig.body.localScale = new Vector3(squish, 1 / squish, squish);
            float leanAmt = wobble * 0.5f;
            Vector3 ll = Quaternion.Inverse(transform.rotation) * lean; // hit direction in the animal's own space
            rig.body.localRotation = Quaternion.Euler((ll.z * leanAmt + f * 0.55f) * Mathf.Rad2Deg, 0, (-ll.x * leanAmt + Mathf.Sin(walk) * 0.05f * (speed > 0.1f ? 1 : 0)) * Mathf.Rad2Deg);
            rig.head.localRotation = Quaternion.Euler(-f * 0.35f * Mathf.Rad2Deg, -f * 0.6f * Mathf.Rad2Deg, 0); // looks back over its shoulder, very pleased with itself
            flash = Mathf.Max(0, flash - dt);
            AnimalModels.SetFlash(rig, flash > 0 ? 0.6f : 0f);
        }
    }
}
