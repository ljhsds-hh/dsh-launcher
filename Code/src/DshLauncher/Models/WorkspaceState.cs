namespace DshLauncher.Models;

/// <summary>
/// 工作目录的校验结果，用于在界面上直接给出「这个目录到底能不能用」的结论，
/// 而不是只把结论写进日志里让人自己找。
/// </summary>
public enum WorkspaceState
{
    /// <summary>用户刚改过路径、还没点「检测」，此时不下结论也不碰磁盘。</summary>
    Unknown,

    /// <summary>已经确认是合法的 DeepSeek Harness 仓库。</summary>
    Valid,

    /// <summary>缺关键文件，不能作为工作目录。</summary>
    Invalid,
}
