using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(UnitAnimateSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ApplyPlayerTintSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (animRef, player) in
                 SystemAPI.Query<UnitAnimatorReference, RefRO<Player>>()
                          .WithChangeFilter<Player>()) // only when value changes
        {
            var go = animRef.Value.gameObject;
            var tint = go.GetComponentInChildren<TintTarget>(true);
            if (tint == null || tint.rendererRef == null) continue;

            var p = player.ValueRO.PlayerColor;
            var mpb = new MaterialPropertyBlock();
            tint.rendererRef.GetPropertyBlock(mpb);
            var c = new Color(p.x, p.y, p.z, p.w);
            mpb.SetColor("_BaseColor", c); // URP
            mpb.SetColor("_Color",     c); // Built-in
            tint.rendererRef.SetPropertyBlock(mpb);
        }

        foreach (var (animRef, player) in
                 SystemAPI.Query<BarracksAnimatorReference, RefRO<Player>>()
                          .WithChangeFilter<Player>()) // only when value changes
        {
            var go = animRef.Value.gameObject;
            var tint = go.GetComponentInChildren<TintTarget>(true);
            if (tint == null || tint.rendererRef == null) continue;

            var p = player.ValueRO.PlayerColor;
            var mpb = new MaterialPropertyBlock();
            tint.rendererRef.GetPropertyBlock(mpb);
            var c = new Color(p.x, p.y, p.z, p.w);
            mpb.SetColor("_BaseColor", c); // URP
            mpb.SetColor("_Color", c); // Built-in
            tint.rendererRef.SetPropertyBlock(mpb);
        }
    }
}