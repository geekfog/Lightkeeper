using System.Text.Json.Nodes;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Routing;
using Lightkeeper.Functions.Tests.TestSupport;

namespace Lightkeeper.Functions.Tests.Cards;

public sealed class AnyEventCardFormatterTests
{
    private readonly AnyEventCardFormatter _formatter = new();

    [Fact]
    public void Work_item_event_uses_azure_devops_messages_project_field_and_web_link()
    {
        var result = _formatter.Format(Samples.Parse(JsonNode.Parse("""
            {
              "id": "evt-1",
              "eventType": "workitem.updated",
              "message": { "text": "Bug #5 (Some great new idea!) updated by Jamal Hartnett." },
              "detailedMessage": { "markdown": "[Bug #5](https://dev.azure.com/fabrikam/web/wi.aspx?id=5) updated\r\n\r\n* State: New → Approved" },
              "resource": {
                "revision": { "fields": { "System.TeamProject": "Fabrikam-Fiber-Git" } },
                "_links": { "html": { "href": "https://dev.azure.com/fabrikam/web/wi.aspx?id=5" } },
                "url": "https://dev.azure.com/fabrikam/_apis/wit/workItems/5"
              }
            }
            """)!));
        var card = result.Card!;

        var header = Assert.IsType<AdaptiveContainer>(card.Body[0]);
        Assert.Equal(["📋 Bug #5 (Some great new idea!) updated by Jamal Hartnett.", "workitem.updated"], header.Items.Cast<AdaptiveTextBlock>().Select(t => t.Text));
        Assert.Equal(new AdaptiveFact(AnyEventCardFormatter.ProjectFactName, "Fabrikam-Fiber-Git"), Assert.Single(Assert.IsType<AdaptiveFactSet>(card.Body[1]).Facts));
        Assert.StartsWith("[Bug #5]", Assert.IsType<AdaptiveTextBlock>(card.Body[2]).Text);
        Assert.Equal(new AdaptiveOpenUrlAction(AnyEventCardFormatter.OpenLinkName, "https://dev.azure.com/fabrikam/web/wi.aspx?id=5"), Assert.Single(card.Actions));
        Assert.Equal("Fabrikam-Fiber-Git", result.Facts!.Project);
        Assert.Equal(RulePattern.RootFolder, result.Facts.Folder);
        Assert.Equal("evt-1", result.Facts.Reference);
    }

    [Fact]
    public void Event_with_no_message_project_or_link_still_produces_a_card()
    {
        var result = _formatter.Format(Samples.Parse(JsonNode.Parse("""{ "eventType": "ms.vss-new.event", "resource": [] }""")!));

        Assert.Equal("🔔 ms.vss-new.event", ((AdaptiveTextBlock)Assert.IsType<AdaptiveContainer>(result.Card!.Body[0]).Items[0]).Text);
        Assert.Empty(result.Card.Actions);
        Assert.Null(result.Facts!.Project);
    }
}
