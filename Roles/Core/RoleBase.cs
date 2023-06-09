using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Hazel;
using AmongUs.GameOptions;

using static TOHE.Translator;

namespace TOHE.Roles.Core;

public abstract class RoleBase : IDisposable
{
    public PlayerControl Player { get; private set; }
    /// <summary>
    /// 玩家状态
    /// </summary>
    public readonly PlayerState MyState;
    /// <summary>
    /// 玩家任务状态
    /// </summary>
    public readonly TaskState MyTaskState;
    /// <summary>
    /// 是否拥有任务
    /// 默认只有在您是船员的时候有任务
    /// </summary>
    protected Func<HasTask> hasTasks;
    /// <summary>
    /// 是否拥有任务
    /// </summary>
    public HasTask HasTasks => hasTasks.Invoke();
    /// <summary>
    /// 任务是否完成
    /// </summary>
    public bool IsTaskFinished => MyTaskState.IsTaskFinished;
    /// <summary>
    /// 可以成为叛徒
    /// </summary>
    public bool CanBeMadmate { get; private set; }
    /// <summary>
    /// 是否拥有技能按钮
    /// </summary>
    public bool HasAbility { get; private set; }
    /// <summary>
    /// 在人数上被计为
    /// </summary>
    public CountTypes CountType => MyState.countTypes;
    public RoleBase(
        SimpleRoleInfo roleInfo,
        PlayerControl player,
        Func<HasTask> hasTasks = null,
        CountTypes? countType = null,
        bool? hasAbility = null,
        bool? canBeMadmate = null
    )
    {
        Player = player;
        this.hasTasks = hasTasks ?? (roleInfo.CustomRoleType == CustomRoleTypes.Crewmate ? () => HasTask.True : () => HasTask.False);
        CanBeMadmate = canBeMadmate ?? Player.Is(CustomRoleTypes.Crewmate);
        HasAbility = hasAbility ?? roleInfo.BaseRoleType.Invoke() is
            RoleTypes.Shapeshifter or
            RoleTypes.Engineer or
            RoleTypes.Scientist or
            RoleTypes.GuardianAngel or
            RoleTypes.CrewmateGhost or
            RoleTypes.ImpostorGhost;

        MyState = PlayerState.GetByPlayerId(player.PlayerId);
        MyTaskState = MyState.GetTaskState();

        MyState.countTypes = countType ?? (roleInfo.RoleName.IsImpostor() ? CountTypes.Impostor : CountTypes.Crew);

        CustomRoleManager.AllActiveRoles.Add(Player.PlayerId, this);
    }
    public void Dispose()
    {
        OnDestroy();
        CustomRoleManager.AllActiveRoles.Remove(Player.PlayerId);
        Player = null;
    }
    public bool Is(PlayerControl player)
    {
        return player.PlayerId == Player.PlayerId;
    }
    /// <summary>
    /// 创建实例后立刻调用的函数
    /// </summary>
    public virtual void Add()
    { }
    /// <summary>
    /// 实例被销毁时调用的函数
    /// </summary>
    public virtual void OnDestroy()
    { }
    /// <summary>
    /// RoleBase 专用 RPC 发送类
    /// 会自动发送 PlayerId
    /// </summary>
    protected class RoleRPCSender : IDisposable
    {
        public MessageWriter Writer;
        public RoleRPCSender(RoleBase role, CustomRPC rpcType)
        {
            Writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)rpcType, SendOption.Reliable, -1);
            Writer.Write(role.Player.PlayerId);
        }
        public void Dispose()
        {
            AmongUsClient.Instance.FinishRpcImmediately(Writer);
        }
    }
    /// <summary>
    /// 创建一个待发送的 RPC
    /// PlayerId 是自动添加的，所以您可以忽略
    /// </summary>
    /// <param name="rpcType">发送的 RPC 类型</param>
    /// <returns>用于发送的 RoleRPCSender</returns>
    protected RoleRPCSender CreateSender(CustomRPC rpcType)
    {
        return new RoleRPCSender(this, rpcType);
    }
    /// <summary>
    /// 接受到 RPC 时的函数
    /// 任何职业收到任何类型的 RPC 时都会调用，所以您需要判断是否是您需要的 RPC 类型
    /// RoleRPCSender 中的 PlayerId 在传递前会被删除，所以您不需要知道它
    /// </summary>
    /// <param name="reader">收到 RPC 内容</param>
    /// <param name="rpcType">收到 RPC 类型</param>
    public virtual void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    { }
    /// <summary>
    /// 可以使用技能按钮
    /// </summary>
    /// <returns>true：可以使用能力按钮</returns>
    public virtual bool CanUseAbilityButton() => true;
    /// <summary>
    /// 在 BuildGameOptions 中调用的函数
    /// </summary>
    public virtual void ApplyGameOptions(IGameOptions opt)
    { }

    /// <summary>
    /// CheckMurder 作为目标处理函数<br/>
    /// 该函数用于您被击杀前的检查，调用该函数前已经调用过 OnCheckMurderAsKiller 函数<br/>
    /// 因此您不需要判断击杀者是否真的尝试击杀你，击杀者对您尝试的击杀是确定的<br/>
    /// 对于无法被击杀的状态（无敌、被保护）等，设置 info.CanKill = false<br/>
    /// 若本次击杀本身就不合法，您可以返回 false 以完全终止本次击杀事件<br/>
    /// </summary>
    /// <param name="info">击杀事件的信息</param>
    /// <returns>false：不再向下执行击杀事件</returns>
    public virtual bool OnCheckMurderAsTarget(MurderInfo info) => true;

    /// <summary>
    /// 已确定本次击杀会发生，但在真正发生前，您想要做点什么吗？
    /// </summary>
    /// <param name="info">击杀事件的信息</param>
    public virtual void BeforeMurderPlayerAsTarget(MurderInfo info)
    { }

    /// <summary>
    /// MurderPlayer 作为目标处理函数
    /// </summary>
    /// <param name="info">击杀事件的信息</param>
    public virtual void OnMurderPlayerAsTarget(MurderInfo info)
    { }

    /// <summary>
    /// 玩家死亡时调用的函数
    /// 无论死亡玩家是谁，所有玩家都会调用，所以您需要判断死亡玩家的身份
    /// </summary>
    /// <param name="player">死亡玩家</param>
    /// <param name="deathReason">死亡原因</param>
    public virtual void OnPlayerDeath(PlayerControl player, CustomDeathReason deathReason, bool isOnMeeting = false)
    { }

    /// <summary>
    /// 变形时调用的函数
    /// 不需要验证您的身份，因为调用前已经验证
    /// 请注意：全部模组端都会调用
    /// </summary>
    /// <param name="shapeshifter">变形目标</param>
    public virtual void OnShapeshift(PlayerControl target)
    { }

    /// <summary>
    /// 帧 Task 处理函数<br/>
    /// 不需要验证您的身份，因为调用前已经验证<br/>
    /// 请注意：全部模组端都会调用<br/>
    /// 如果您想在帧 Task 处理不是您自己时进行处理，请使用相同的参数将其实现为静态
    /// 并注册为 CustomRoleManager.OnFixedUpdateOthers
    /// </summary>
    /// <param name="player">目标玩家</param>
    public virtual void OnFixedUpdate(PlayerControl player)
    { }
    /// <summary>
    /// 秒 Task 处理函数<br/>
    /// 不需要验证您的身份，因为调用前已经验证<br/>
    /// 请注意：全部模组端都会调用<br/>
    /// </summary>
    /// <param name="player">目标玩家</param>
    /// <param name="now">当前10位时间戳</param>
    public virtual void OnSecondsUpdate(PlayerControl player, long now)
    { }

    /// <summary>
    /// 报告前检查调用的函数
    /// 与报告事件无关的玩家也会调用该函数
    /// </summary>
    /// <param name="reporter">报告者</param>
    /// <param name="target">被报告的玩家</param>
    /// <returns>false：取消报告</returns>
    public virtual bool OnCheckReportDeadBody(PlayerControl reporter, GameData.PlayerInfo target) => true;

    /// <summary>
    /// 报告时调用的函数
    /// 与报告事件无关的玩家也会调用该函数
    /// </summary>
    /// <param name="reporter">报告者</param>
    /// <param name="target">被报告的玩家</param>
    public virtual void OnReportDeadBody(PlayerControl reporter, GameData.PlayerInfo target)
    { }

    /// <summary>
    /// <para>进入通风管时调用的函数</para>
    /// <para>可以取消</para>
    /// </summary>
    /// <param name="physics"></param>
    /// <param name="id">通风管 ID</param>
    /// <returns>false：将玩家被踢出通风管，其他人将看不到动画。</returns>
    public virtual bool OnEnterVent(PlayerPhysics physics, int ventId) => true;

    /// <summary>
    /// 会议开始时调用的函数
    /// </summary>
    public virtual void OnStartMeeting()
    { }

    /// <summary>
    /// 玩家投票时调用的函数
    /// </summary>
    /// <param name="statesList">储存全部投票信息的列表</param>
    /// <param name="pva">玩家</param>
    /// <returns>false：不继续向下执行原版的会议结束判断</returns>
    public virtual bool OnCheckForEndVoting(ref List<MeetingHud.VoterState> statesList, PlayerVoteArea pva) => true;

    /// <summary>
    /// 在投票结束时对全部投票信息进行预处理
    /// </summary>
    /// <param name="statesList">全部投票信息</param>
    /// <param name="pva">投票信息</param>
    /// <returns>false：忽略该投票</returns>
    public virtual bool OnVotingEnd(ref List<MeetingHud.VoterState> statesList, ref PlayerVoteArea pva) => true;

    /// <summary>
    /// 计算每位玩家投票的函数
    /// </summary>
    /// <param name="ps">投票信息</param>
    /// <param name="VoteNums">结算票数</param>
    public virtual void OnCalculateVotes(ref PlayerVoteArea ps, ref int VoteNum)
    { }

    /// <summary>
    /// 驱逐玩家后调用的函数
    /// </summary>
    /// <param name="exiled">被驱逐的玩家</param>
    /// <param name="DecidedWinner">是否决定了胜利玩家</param>
    public virtual void OnExileWrapUp(GameData.PlayerInfo exiled, ref bool DecidedWinner)
    { }

    /// <summary>
    /// 每次会议结束后调用的函数
    /// </summary>
    public virtual void AfterMeetingTasks()
    { }

    /// <summary>
    /// 玩家发送消息后调用的函数
    /// </summary>
    /// <param name="msg">收到的消息内容</param>
    /// <returns>true：阻塞该消息，并不继续向下判断</returns>
    public virtual bool OnReceiveMessage(string msg) => false;

    /// <summary>
    /// 每次任务完成时调用的函数
    /// 设置 cancel=true 后返回 true 来取消原版的任务完成处理
    /// </summary>
    /// <returns>true：确认覆盖</returns>
    public virtual bool OnCompleteTask(out bool cancel)
    {
        cancel = default;
        return false;
    }

    // == 破坏相关处理 ==
    /// <summary>
    /// 是否可以破坏
    /// 若禁止将无法关门
    /// </summary>
    /// <param name="systemType">破坏的设施类型</param>
    /// <returns>false：取消破坏</returns>
    public virtual bool CanSabotage(SystemTypes systemType) => true;

    /// <summary>
    /// 当有人造成破坏时调用
    /// amount&0x80!=0 则为破坏开始
    /// 其他数值则是修复
    /// </summary>
    /// <param name="player">操作的玩家</param>
    /// <param name="systemType">操作的设施类型</param>
    /// <param name="amount">当前状态</param>
    /// <returns>false：取消本次操作</returns>
    public virtual bool OnSabotage(PlayerControl player, SystemTypes systemType, byte amount) => true;

    // NameSystem
    // 显示的名字结构如下
    // [Role][Progress]
    // [Name][Mark]
    // [Lower][suffix]
    // Progress：任务进度、剩余子弹等信息
    // Mark：通过位置能力等进行目标标记
    // Lower：附加文本信息，模组端则会显示在屏幕下方
    // Suffix：其他信息，例如箭头

    /// <summary>
    /// 作为 seen 重写 RoleName
    /// </summary>
    /// <param name="seer">将要看到您的 RoleName 的玩家</param>
    /// <param name="enabled">是否显示 RoleName</param>
    /// <param name="roleColor">RoleName 的颜色</param>
    /// <param name="roleText">RoleName 的文本</param>
    public virtual void OverrideRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText)
    { }
    /// <summary>
    /// 作为 seer 重写 RoleName
    /// </summary>
    /// <param name="seen">您将要看到其 RoleName 的玩家</param>
    /// <param name="enabled">是否显示 RoleName</param>
    /// <param name="roleColor">RoleName 的颜色</param>
    /// <param name="roleText">RoleName 的文本</param>
    public virtual void OverrideRoleNameAsSeer(PlayerControl seen, ref bool enabled, ref Color roleColor, ref string roleText)
    { }
    /// <summary>
    /// 作为 seer 重写 ProgressText
    /// </summary>
    /// <param name="seen">您将要看到其 ProgressText 的玩家</param>
    /// <param name="enabled">是否显示 ProgressText</param>
    /// <param name="text">ProgressText 的文本</param>
    public virtual void OverrideProgressTextAsSeer(PlayerControl seen, ref bool enabled, ref string text)
    { }
    /// <summary>
    /// 显示在职业旁边的文本
    /// </summary>
    /// <param name="comms">目前是否为通讯破坏状态</param>
    public virtual string GetProgressText(bool comms = false) => "";
    /// <summary>
    /// 作为 seen 重写 Name
    /// </summary>
    /// <param name="seer">将要看到您的 Name 的玩家</param>
    /// <param name="nameText">Name 的文本</param>
    /// <param name="isForMeeting">是否用于显示在会议上</param>
    public virtual void OverrideNameAsSeen(PlayerControl seer, ref string nameText, bool isForMeeting = false)
    { }
    /// <summary>
    /// 作为 seer 重写 Name
    /// </summary>
    /// <param name="seen">您将要看到其 Name 的玩家</param>
    /// <param name="roleColor">Name 的颜色</param>
    /// <param name="nameText">Name 的文本</param>
    /// <param name="isForMeeting">是否用于显示在会议上</param>
    public virtual void OverrideNameAsSeer(PlayerControl seen, ref string nameText, bool isForMeeting = false)
    { }
    /// <summary>
    /// 作为 seer 时获取 Mark 的函数
    /// 如果您想在 seer,seen 都不是您时进行处理，请使用相同的参数将其实现为静态
    /// 并注册为 CustomRoleManager.MarkOthers
    /// </summary>
    /// <param name="seer">看到的人</param>
    /// <param name="seen">被看到的人</param>
    /// <param name="isForMeeting">是否正在会议中</param>
    /// <returns>構築したMark</returns>
    public virtual string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false) => "";
    /// <summary>
    /// 作为 seer 时获取 LowerTex 的函数
    /// 如果您想在 seer,seen 都不是您时进行处理，请使用相同的参数将其实现为静态
    /// 并注册为 CustomRoleManager.LowerOthers
    /// </summary>
    /// <param name="seer">看到的人</param>
    /// <param name="seen">被看到的人</param>
    /// <param name="isForMeeting">是否正在会议中</param>
    /// <param name="isForHud">是否显示在模组端的HUD</param>
    /// <returns>组合后的全部 LowerText</returns>
    public virtual string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false) => "";
    /// <summary>
    /// 作为 seer 时获取 LowerTex 的函数
    /// 如果您想在 seer,seen 都不是您时进行处理，请使用相同的参数将其实现为静态
    /// 并注册为 CustomRoleManager.SuffixOthers
    /// </summary>
    /// <param name="seer">看到的人</param>
    /// <param name="seen">被看到的人</param>
    /// <param name="isForMeeting">是否正在会议中</param>
    /// <returns>组合后的全部 Suffix</returns>
    public virtual string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false) => "";

    /// <summary>
    /// 修改 HUD Manager 的任意属性
    /// </summary>
    /// <param name="__instance">HudManager 的实例</param>
    public virtual void ChangeHudManager(HudManager __instance)
    { }

    /// <summary>
    /// 更改变形按钮的文本
    /// </summary>
    public virtual bool OverrideAbilityButtonText(out string text)
    {
        text = default;
        return false;
        //StringNames? str = Player.Data.Role.Role switch
        //{
        //    RoleTypes.Engineer => StringNames.VentAbility,
        //    RoleTypes.Scientist => StringNames.VitalsAbility,
        //    RoleTypes.Shapeshifter => StringNames.ShapeshiftAbility,
        //    RoleTypes.GuardianAngel => StringNames.ProtectAbility,
        //    RoleTypes.ImpostorGhost or RoleTypes.CrewmateGhost => StringNames.HauntAbilityName,
        //    _ => null
        //};
        //return str.HasValue ? GetString(str.Value) : "Invalid";
    }

    /// <summary>
    /// 更改报告按钮文本
    /// </summary>
    /// <param name="text">覆盖后的文本</param>
    /// <returns>true：确定要覆盖</returns>
    public virtual string GetReportButtonText() => GetString(StringNames.ReportLabel);

    /// <summary>
    /// 会议开始时的全部提示信息
    /// </summary>
    /// <param name="msgToSend">等待发送的信息列表</param>
    public virtual void NotifyOnMeetingStart(ref List<(string, byte, string)> msgToSend)
    { }

    /// <summary>
    /// 游戏开始后会立刻调用该函数
    /// 默认为全体玩家调用
    /// </summary>
    public virtual void OnGameStart()
    { }

    protected static AudioClip GetIntroSound(RoleTypes roleType) =>
        RoleManager.Instance.AllRoles.Where((role) => role.Role == roleType).FirstOrDefault().IntroSound;

    protected enum GeneralOption
    {
        Cooldown,
        KillCooldown,
        CanVent,
        ImpostorVision,
        CanUseSabotage,
        CanCreateMadmate,
        CanKillAllies,
        CanKillSelf,
        ShapeshiftDuration,
        ShapeshiftCooldown,
    }
}