namespace Leaf.Utils;

/// <summary>
/// Shared content analysis utilities.
/// </summary>
public static class ContentUtils
{
    /// <summary>
    /// Checks if content is binary by looking for null bytes in the first 8KB.
    /// </summary>
    public static bool IsBinaryContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        var checkLength = Math.Min(content.Length, 8192);
        for (int i = 0; i < checkLength; i++)
        {
            if (content[i] == '\0')
                return true;
        }

        return false;
    }

    /// <summary>
    /// Counts the number of lines in content.
    /// </summary>
    public static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        int count = 1;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
                count++;
        }
        return count;
    }
}
