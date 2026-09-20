namespace DshLauncher.Models;

/// <summary>单个步骤的运行状态。</summary>
public enum StepState
{
    /// <summary>尚未执行。</summary>
    Waiting,

    /// <summary>正在执行（或常驻服务正在运行）。</summary>
    Running,

    /// <summary>执行成功。</summary>
    Succeeded,

    /// <summary>执行失败。</summary>
    Failed,

    /// <summary>被用户中止。</summary>
    Cancelled,
}
