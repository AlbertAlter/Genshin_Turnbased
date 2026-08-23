using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>整套配表预检或加载失败。Errors 保存可一次性展示的具体原因。</summary>
public sealed class DataLoadException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public DataLoadException(string title, IEnumerable<string> errors)
        : this(title, errors, null)
    {
    }

    public DataLoadException(string title, IEnumerable<string> errors, Exception innerException)
        : base(BuildMessage(title, errors), innerException)
    {
        Errors = (errors ?? Array.Empty<string>()).ToArray();
    }

    private static string BuildMessage(string title, IEnumerable<string> errors)
    {
        string[] details = (errors ?? Array.Empty<string>()).Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
        if (details.Length == 0)
            return title;

        return title + Environment.NewLine + string.Join(Environment.NewLine, details.Select(error => "- " + error));
    }
}
