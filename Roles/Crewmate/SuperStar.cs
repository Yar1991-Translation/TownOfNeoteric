﻿using AmongUs.GameOptions;
using System.Linq;
using TOHE.Roles.Core;
using UnityEngine;

namespace TOHE.Roles.Crewmate;
public sealed class SuperStar : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        new(
            typeof(SuperStar),
            player => new SuperStar(player),
            CustomRoles.SuperStar,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            8020165,
            SetupOptionItem,
            "ss",
            "#f6f657"
        );
    public SuperStar(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        CustomRoleManager.MarkOthers.Add(MarkOthers);
    }

    public static OptionItem OptionEveryoneKnowSuperStar;
    enum OptionName
    {
        EveryOneKnowSuperStar,
    }

    private static void SetupOptionItem()
    {
        OptionEveryoneKnowSuperStar = BooleanOptionItem.Create(RoleInfo, 10, OptionName.EveryOneKnowSuperStar, true, false);
    }
    public static string MarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        return (seen.Is(CustomRoles.SuperStar) && OptionEveryoneKnowSuperStar.GetBool()) ? Utils.ColorString(RoleInfo.RoleColor, "★") : "";
    }
    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        if (info.IsSuicide) return true;
        return !Main.AllAlivePlayerControls.Any(pc =>
            !Is(pc) &&
            pc != killer &&
            Vector2.Distance(pc.GetTruePosition(), target.GetTruePosition()) < 2f
            );
    }
}