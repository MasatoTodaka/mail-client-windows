using MailClient.Core.Models;

namespace MailClient.ViewModels.Rules;

// Pairs a rule with its target folder's display name, resolved once at load time — DataTemplates
// bound to this only need members of the item itself, not a lookup back into the ViewModel.
public sealed record RuleDisplayItem(MailRule Rule, string TargetFolderName)
{
    public string Summary
    {
        get
        {
            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(Rule.SenderContains))
                conditions.Add($"送信者に「{Rule.SenderContains}」を含む");
            if (!string.IsNullOrWhiteSpace(Rule.SubjectContains))
                conditions.Add($"件名に「{Rule.SubjectContains}」を含む");
            return $"{string.Join(" かつ ", conditions)} → {TargetFolderName}";
        }
    }
}
