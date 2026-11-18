using System.Numerics;
using AimLab.Core;
using AimLab.Entities;

namespace AimLab.Tests;

internal static class TestData
{
    public static PlayerEntity Player(
        int id, Team team, Vector3 origin, int health = 100, bool visible = true, ViewAngles view = default) =>
        new(id, id, team, health, health > 0, visible, origin,
            origin + new Vector3(0, 0, 64), origin + new Vector3(0, 0, 38), view);

    public static LocalPlayerState Local(Vector3 origin, ViewAngles view, Team team = Team.Defenders)
    {
        var state = new LocalPlayerState();
        state.Update(Player(1, team, origin, view: view));
        return state;
    }
}
