using System.Text.Json;
using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

public class IssueEventPayloadTests
{
    private static EfHatchStatus Column(int id, string name) => new() { Id = id, Name = name, SortOrder = id * 10 };

    [Fact]
    public void StatusChanged_NamesAndNumbersBothColumns()
    {
        var payload = IssueEventPayload.StatusChanged(Column(3, "todo"), Column(4, "doing"));

        var json = JsonSerializer.SerializeToElement(payload);
        Assert.Equal("todo", json.GetProperty("from").GetString());
        Assert.Equal("doing", json.GetProperty("to").GetString());
        Assert.Equal(3, json.GetProperty("fromId").GetInt32());
        Assert.Equal(4, json.GetProperty("toId").GetInt32());
    }

    [Fact]
    public void StatusChanged_WithNoPreviousColumn_SerialisesFromAndFromIdAsNull()
    {
        var payload = IssueEventPayload.StatusChanged(null, Column(4, "doing"));

        var json = JsonSerializer.SerializeToElement(payload);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("fromId").ValueKind);
        Assert.Equal("doing", json.GetProperty("to").GetString());
        Assert.Equal(4, json.GetProperty("toId").GetInt32());
    }

    [Fact]
    public void StatusChanged_AnExtraPair_LandsBesideTheFourKeys()
    {
        var payload = IssueEventPayload.StatusChanged(Column(3, "todo"), Column(4, "doing"), ("expedited", true));

        Assert.Equal(["from", "to", "fromId", "toId", "expedited"], payload.Keys);
        var json = JsonSerializer.SerializeToElement(payload);
        Assert.True(json.GetProperty("expedited").GetBoolean());
    }

    [Fact]
    public void ContributionFiled_NamesEveryField_InOrder_WithNullsKept()
    {
        var payload = IssueEventPayload.ContributionFiled(new EfHatchContribution
        {
            IssueId = 1, PullRequestUrl = null, HeadBranch = null, AuthorLogin = "octocat",
            AuthorAssociation = "NONE", FiledAt = DateTimeOffset.UnixEpoch, FiledBy = "runner-1",
            OriginInstance = "https://hatch.example", OriginProjectKey = "OTH", OriginKey = "OTH-4",
        });

        Assert.Equal(
            ["pullRequestUrl", "headBranch", "authorLogin", "authorAssociation", "originInstance", "originProjectKey", "originKey"],
            payload.Keys);
        var json = JsonSerializer.SerializeToElement(payload);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("pullRequestUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("headBranch").ValueKind);
        Assert.Equal("octocat", json.GetProperty("authorLogin").GetString());
        Assert.Equal("OTH-4", json.GetProperty("originKey").GetString());
    }
}
