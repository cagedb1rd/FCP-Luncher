namespace FCP.Core;

/// <summary>
/// 面向用户的错误：标题 + 用大白话写的说明（发生了什么 → 原因 → 怎么办）。
/// 界面直接显示 <see cref="Title"/> 与 <see cref="Exception.Message"/>，技术细节放在 InnerException 中。
/// </summary>
public sealed class UserFacingException(string title, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Title { get; } = title;
}
