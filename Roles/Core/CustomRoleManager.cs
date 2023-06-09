using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Hazel;
using Il2CppSystem.Text;

using AmongUs.GameOptions;
using TOHE.Roles.Core.Interfaces;

namespace TOHE.Roles.Core;

public static class CustomRoleManager
{
    public static Type[] AllRolesClassType;
    public static Dictionary<CustomRoles, SimpleRoleInfo> AllRolesInfo = new(CustomRolesHelper.AllRoles.Length);
    public static Dictionary<byte, RoleBase> AllActiveRoles = new(15);

    public static SimpleRoleInfo GetRoleInfo(this CustomRoles role) => AllRolesInfo.ContainsKey(role) ? AllRolesInfo[role] : null;
    public static RoleBase GetRoleClass(this PlayerControl player) => GetByPlayerId(player.PlayerId);
    public static RoleBase GetByPlayerId(byte playerId) => AllActiveRoles.TryGetValue(playerId, out var roleBase) ? roleBase : null;
    public static void Do<T>(this List<T> list, Action<T> action) => list.ToArray().Do(action);
    // == CheckMurder 相关处理 ==
    public static Dictionary<byte, MurderInfo> CheckMurderInfos = new();
    /// <summary>
    ///
    /// </summary>
    /// <param name="attemptKiller">实际击杀者，不变</param>
    /// <param name="attemptTarget">>实际被击杀的玩家，不变</param>
    public static void OnCheckMurder(PlayerControl attemptKiller, PlayerControl attemptTarget)
        => OnCheckMurder(attemptKiller, attemptTarget, attemptKiller, attemptTarget);
    /// <summary>
    ///
    /// </summary>
    /// <param name="attemptKiller">实际击杀者，不变</param>
    /// <param name="attemptTarget">>实际被击杀的玩家，不变</param>
    /// <param name="appearanceKiller">视觉上的击杀者，可变</param>
    /// <param name="appearanceTarget">视觉上被击杀的玩家，可变</param>
    public static void OnCheckMurder(PlayerControl attemptKiller, PlayerControl attemptTarget, PlayerControl appearanceKiller, PlayerControl appearanceTarget, Action actionAfterAll = null)
    {
        Logger.Info($"Attempt：{attemptKiller.GetNameWithRole()} => {attemptTarget.GetNameWithRole()}", "CheckMurder");
        if (appearanceKiller != attemptKiller || appearanceTarget != attemptTarget)
            Logger.Info($"Apperance：{appearanceKiller.GetNameWithRole()} => {appearanceTarget.GetNameWithRole()}", "CheckMurder");

        var info = new MurderInfo(attemptKiller, attemptTarget, appearanceKiller, appearanceTarget);

        appearanceKiller.ResetKillCooldown();

        // 無効なキルをブロックする処理 必ず最初に実行する
        if (!CheckMurderPatch.CheckForInvalidMurdering(info)) return;

        var killerRole = attemptKiller.GetRoleClass();
        var targetRole = attemptTarget.GetRoleClass();

        // 其他职业类对击杀事件的事先检查
        foreach (var onCheckMurderPlayer in OnCheckMurderPlayerOthers_Before)
        {
            if (!onCheckMurderPlayer(info)) return;
        }

        // キラーがキル能力持ちなら
        if (killerRole is IKiller killer)
        {
            // キラーのキルチェック処理実行
            if (!killer.OnCheckMurderAsKiller(info)) return;
            if (killer.IsKiller && targetRole != null)
            {
                // ターゲットのキルチェック処理実行
                if (!targetRole.OnCheckMurderAsTarget(info)) return;
            }
        }

        // 其他职业类对击杀事件的事后检查
        foreach (var onCheckMurderPlayer in OnCheckMurderPlayerOthers_After)
        {
            if (!onCheckMurderPlayer(info)) return;
        }

        // 调用职业类对击杀发生前进行预处理如设置冷却等操作
        if (killerRole is IKiller) (killerRole as IKiller)?.BeforeMurderPlayerAsKiller(info);
        targetRole.BeforeMurderPlayerAsTarget(info);

        //キル可能だった場合のみMurderPlayerに進む
        if (info.CanKill && info.DoKill)
        {
            //MurderPlayer用にinfoを保存
            CheckMurderInfos[appearanceKiller.PlayerId] = info;
            appearanceKiller.RpcMurderPlayer(appearanceTarget);
            if (actionAfterAll != null) actionAfterAll();
        }
        else
        {
            if (!info.CanKill) Logger.Info($"{appearanceTarget.GetNameWithRole()} 无法被击杀", "CheckMurder");
            if (!info.DoKill) Logger.Info($"{appearanceKiller.GetNameWithRole()} 无法击杀", "CheckMurder");
        }
    }
    /// <summary>
    /// MurderPlayer 事件的处理
    /// </summary>
    /// <param name="appearanceKiller">视觉上的击杀者，可变</param>
    /// <param name="appearanceTarget">视觉上被击杀的玩家，可变</param>
    public static void OnMurderPlayer(PlayerControl appearanceKiller, PlayerControl appearanceTarget)
    {
        //MurderInfoの取得
        if (CheckMurderInfos.TryGetValue(appearanceKiller.PlayerId, out var info))
        {
            //参照出来たら削除
            CheckMurderInfos.Remove(appearanceKiller.PlayerId);
        }
        else
        {
            //CheckMurderを経由していない場合はappearanceで処理
            info = new MurderInfo(appearanceKiller, appearanceTarget, appearanceKiller, appearanceTarget);
        }

        (var attemptKiller, var attemptTarget) = info.AttemptTuple;

        Logger.Info($"Real Killer={attemptKiller.GetNameWithRole()}", "MurderPlayer");

        //キラーの処理
        (attemptKiller.GetRoleClass() as IKiller)?.OnMurderPlayerAsKiller(info);

        //ターゲットの処理
        var targetRole = attemptTarget.GetRoleClass();
        targetRole?.OnMurderPlayerAsTarget(info);

        //その他視点の処理があれば実行
        foreach (var onMurderPlayer in OnMurderPlayerOthers.ToArray())
        {
            onMurderPlayer(info);
        }

        //サブロール処理ができるまではラバーズをここで処理
        FixedUpdatePatch.LoversSuicide(attemptTarget.PlayerId);

        //以降共通処理
        var targetState = PlayerState.GetByPlayerId(attemptTarget.PlayerId);
        if (targetState.DeathReason == CustomDeathReason.etc)
        {
            //死因が設定されていない場合は死亡判定
            targetState.DeathReason = CustomDeathReason.Kill;
        }

        targetState.SetDead();
        attemptTarget.SetRealKiller(attemptKiller, true);

        Utils.CountAlivePlayers(true);

        Utils.TargetDies(info);

        Utils.SyncAllSettings();
        Utils.NotifyRoles();
    }
    /// <summary>
    /// 其他玩家视角下的 MurderPlayer 事件
    /// 初始化时使用 OnMurderPlayerOthers+= 注册
    /// </summary>
    public static HashSet<Action<MurderInfo>> OnMurderPlayerOthers = new();
    /// <summary>
    /// 其他玩家视角下的 CheckMurderPlayer 事件
    /// 在击杀事件当时玩家的 CheckMurderPlayer 函数调用前检查
    /// 初始化时使用 OnCheckMurderPlayerOthers_Before+= 注册
    /// 返回 false 以阻止本次击杀事件
    /// </summary>
    public static HashSet<Func<MurderInfo, bool>> OnCheckMurderPlayerOthers_Before = new();
    /// <summary>
    /// 其他玩家视角下的 CheckMurderPlayer 事件
    /// 在击杀事件当时玩家的 CheckMurderPlayer 函数调用后检查
    /// 初始化时使用 OnCheckMurderPlayerOthers_After+= 注册
    /// 返回 false 以阻止本次击杀事件
    /// </summary>
    public static HashSet<Func<MurderInfo, bool>> OnCheckMurderPlayerOthers_After = new();

    private static Dictionary<byte, long> LastSecondsUpdate = new();
    public static void OnFixedUpdate(PlayerControl player)
    {
        if (GameStates.IsInTask)
        {
            var now = Utils.GetTimeStamp();
            LastSecondsUpdate.TryAdd(player.PlayerId, 0);
            if (LastSecondsUpdate[player.PlayerId] != now)
            {
                player.GetRoleClass()?.OnSecondsUpdate(player, now);
                LastSecondsUpdate[player.PlayerId] = now;
            }

            player.GetRoleClass()?.OnFixedUpdate(player);
            //その他視点処理があれば実行
            foreach (var onFixedUpdate in OnFixedUpdateOthers)
            {
                onFixedUpdate(player);
            }
        }
    }
    /// <summary>
    /// 其他玩家视角下的帧 Task 处理事件
    /// 用于干涉其他职业
    /// 请注意：全部模组端都会调用
    /// 初始化时使用 OnFixedUpdateOthers+= 注册
    /// </summary>
    public static HashSet<Action<PlayerControl>> OnFixedUpdateOthers = new();

    public static bool OnSabotage(PlayerControl player, SystemTypes systemType, byte amount)
    {
        bool cancel = false;
        foreach (var roleClass in AllActiveRoles.Values)
        {
            if (!roleClass.OnSabotage(player, systemType, amount))
            {
                cancel = true;
            }
        }
        return !cancel;
    }
    // ==初始化处理 ==
    public static void Initialize()
    {
        AllRolesInfo.Do(kvp => kvp.Value.IsEnable = kvp.Key.IsEnable());
        AllActiveRoles.Clear();
        MarkOthers.Clear();
        LowerOthers.Clear();
        SuffixOthers.Clear();
        CheckMurderInfos.Clear();
        OnMurderPlayerOthers.Clear();
        OnCheckMurderPlayerOthers_Before.Clear();
        OnCheckMurderPlayerOthers_After.Clear();
        OnFixedUpdateOthers.Clear();
    }
    public static void CreateInstance()
    {
        foreach (var pc in Main.AllPlayerControls)
        {
            CreateInstance(pc.GetCustomRole(), pc);
        }
    }
    public static void CreateInstance(CustomRoles role, PlayerControl player)
    {
        if (AllRolesInfo.TryGetValue(role, out var roleInfo))
        {
            roleInfo.CreateInstance(player).Add();
        }
        else
        {
            OtherRolesAdd(player);
        }
        if (player.Data.Role.Role == RoleTypes.Shapeshifter) Main.CheckShapeshift.Add(player.PlayerId, false);

    }
    public static void OtherRolesAdd(PlayerControl pc)
    {
        foreach (var subRole in pc.GetCustomSubRoles())
        {
            switch (subRole)
            {
                //TODO: FIXME
            }
        }
    }
    /// <summary>
    /// 从收到的RPC中取得目标并传给职业类
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="rpcType"></param>
    public static void DispatchRpc(MessageReader reader, CustomRPC rpcType)
    {
        var playerId = reader.ReadByte();
        GetByPlayerId(playerId)?.ReceiveRPC(reader, rpcType);
    }
    //NameSystem
    public static HashSet<Func<PlayerControl, PlayerControl, bool, string>> MarkOthers = new();
    public static HashSet<Func<PlayerControl, PlayerControl, bool, bool, string>> LowerOthers = new();
    public static HashSet<Func<PlayerControl, PlayerControl, bool, string>> SuffixOthers = new();
    /// <summary>
    /// 无论 seer,seen 是否持有职业职业都会触发的 Mark 获取事件
    /// 会默认为全体职业注册
    /// </summary>
    /// <param name="seer">看到的人</param>
    /// <param name="seen">被看到的人</param>
    /// <param name="isForMeeting">是否正在会议中</param>
    /// <returns>组合后的全部 Mark</returns>
    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        var sb = new StringBuilder(100);
        foreach (var marker in MarkOthers)
        {
            sb.Append(marker(seer, seen, isForMeeting));
        }
        return sb.ToString();
    }
    /// <summary>
    /// 无论 seer,seen 是否持有职业职业都会触发的 LowerText 获取事件
    /// 会默认为全体职业注册
    /// </summary>
    /// <param name="seer">看到的人</param>
    /// <param name="seen">被看到的人</param>
    /// <param name="isForMeeting">是否正在会议中</param>
    /// <param name="isForHud">是否显示在模组端的HUD</param>
    /// <returns>组合后的全部 LowerText</returns>
    public static string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        var sb = new StringBuilder(100);
        foreach (var lower in LowerOthers)
        {
            sb.Append(lower(seer, seen, isForMeeting, isForHud));
        }
        return sb.ToString();
    }
    /// <summary>
    /// 无论 seer,seen 是否持有职业职业都会触发的 Suffix 获取事件
    /// 会默认为全体职业注册
    /// </summary>
    /// <param name="seer">看到的人</param>
    /// <param name="seen">被看到的人</param>
    /// <param name="isForMeeting">是否正在会议中</param>
    /// <returns>组合后的全部 Suffix</returns>
    public static string GetSuffixOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        var sb = new StringBuilder(100);
        foreach (var suffix in SuffixOthers)
        {
            sb.Append(suffix(seer, seen, isForMeeting));
        }
        return sb.ToString();
    }
    /// <summary>
    /// 全部对象的销毁事件
    /// </summary>
    public static void Dispose()
    {
        Logger.Info($"Dispose ActiveRoles", "CustomRoleManager");
        MarkOthers.Clear();
        LowerOthers.Clear();
        SuffixOthers.Clear();
        CheckMurderInfos.Clear();
        OnMurderPlayerOthers.Clear();
        OnFixedUpdateOthers.Clear();

        AllActiveRoles.Values.ToArray().Do(roleClass => roleClass.Dispose());
    }
}
public class MurderInfo
{
    /// <summary>实际击杀者，不变</summary>
    public PlayerControl AttemptKiller { get; }
    /// <summary>实际被击杀的玩家，不变</summary>
    public PlayerControl AttemptTarget { get; }
    /// <summary>视觉上的击杀者，可变</summary>
    public PlayerControl AppearanceKiller { get; set; }
    /// <summary>视觉上被击杀的玩家，可变</summary>
    public PlayerControl AppearanceTarget { get; set; }

    /// <summary>
    /// 目标是否可以被击杀，由于目标导致的无法击杀将该值赋值为 false
    /// </summary>
    public bool CanKill = true;
    /// <summary>
    /// 击杀者是否真的会进行击杀，由于击杀者导致的无法击杀将该值赋值为 false
    /// </summary>
    public bool DoKill = true;
    /// <summary>
    /// 是否发生从梯子上摔死等意外
    /// </summary>
    public bool IsAccident = false;

    // 使用 (killer, target) = info.AttemptTuple; 即可获得数据
    public (PlayerControl killer, PlayerControl target) AttemptTuple => (AttemptKiller, AttemptTarget);
    public (PlayerControl killer, PlayerControl target) AppearanceTuple => (AppearanceKiller, AppearanceTarget);
    /// <summary>
    /// 真的是自杀
    /// </summary>
    public bool IsSuicide => AttemptKiller.PlayerId == AttemptTarget.PlayerId;
    /// <summary>
    /// 原版视角下的自杀
    /// </summary>
    public bool IsFakeSuicide => AppearanceKiller.PlayerId == AppearanceTarget.PlayerId;
    public MurderInfo(PlayerControl attemptKiller, PlayerControl attemptTarget, PlayerControl appearanceKiller, PlayerControl appearancetarget)
    {
        AttemptKiller = attemptKiller;
        AttemptTarget = attemptTarget;
        AppearanceKiller = appearanceKiller;
        AppearanceTarget = appearancetarget;
    }
}

public enum CustomRoles
{
    //Default
    Crewmate = 0,
    //Impostor(Vanilla)
    Impostor,
    Shapeshifter,
    //Impostor
    BountyHunter,
    FireWorks,
    Mafia,
    SerialKiller,
    ShapeMaster,
    EvilGuesser,
    Minimalism,
    Zombie,
    Sniper,
    Vampire,
    Witch,
    Warlock,
    Assassin,
    Hacker,
    Miner,
    Escapee,
    Mare,
    Puppeteer,
    TimeThief,
    EvilTracker,
    AntiAdminer,
    Sans,
    Bomber,
    BoobyTrap,
    Scavenger,
    Capitalism,
    Gangster,
    Cleaner,
    BallLightning,
    Greedier,
    CursedWolf,
    ImperiusCurse,
    QuickShooter,
    Concealer,
    Eraser,
    OverKiller,
    Hangman,
    Bard,
    Swooper,
    Crewpostor,
    //Crewmate(Vanilla)
    Engineer,
    GuardianAngel,
    Scientist,
    //Crewmate
    Luckey,
    Needy,
    SuperStar,
    CyberStar,
    Mayor,
    Paranoia,
    Psychic,
    SabotageMaster,
    Sheriff,
    Snitch,
    SpeedBooster,
    Dictator,
    Doctor,
    Detective,
    SwordsMan,
    NiceGuesser,
    Transporter,
    TimeManager,
    Veteran, //TODO
    Bodyguard,
    Counterfeiter, //TODO
    Grenadier, //TODO
    Medicaler, //TODO
    Divinator,
    Glitch,
    Judge,
    Mortician,
    Mediumshiper, //TODO
    Observer,
    DovesOfNeace, //TODO
    //Neutral
    Arsonist,
    Jester,
    God,
    Opportunist,
    Mario, //TODO
    Terrorist,
    Executioner,
    Jackal,
    Innocent, //TODO
    Pelican, //TODO
    Revolutionist, //TODO
    FFF, //TODO
    Konan, //TODO
    Gamer, //TODO
    DarkHide, //TODO
    Workaholic, //TODO
    Collector, //TODO
    Provocateur, //TODO
    Sunnyboy, //TODO
    BloodKnight, //TODO
    Totocalcio, //TODO
    Succubus, //TODO

    //SoloKombat
    KB_Normal, //TODO

    //GM
    GM,

    //Sub-role after 500
    NotAssigned = 500,
    LastImpostor,
    Lovers,
    Ntr, //TODO
    Madmate, //TODO
    Watcher,
    Flashman, //TODO
    Lighter, //TODO
    Seer, //TODO
    Brakar, //TODO
    Oblivious, //TODO
    Bewilder, //TODO
    Workhorse, //TODO
    Fool, //TODO
    Avanger, //TODO
    Youtuber, //TODO
    Egoist, //TODO
    TicketsStealer, //TODO,
    DualPersonality, //TODO
    Mimic, //TODO
    Reach, //TODO
    Charmed, //TODO
    Bait, //TODO
    Trapper, //TODO
}
public enum CustomRoleTypes
{
    Crewmate,
    Impostor,
    Neutral,
    Addon
}
public enum HasTask
{
    True,
    False,
    ForRecompute
}