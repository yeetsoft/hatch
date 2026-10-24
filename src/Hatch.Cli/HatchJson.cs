using System.Text.Json.Serialization;

namespace Hatch.Cli;

/// <summary>
/// Every record that crosses the wire, with its reader and writer generated at
/// compile time instead of discovered by reflection at runtime.
/// </summary>
/// <remarks>
/// <para>Not an optimisation. <c>make publish-hatch</c> trims the binary, and a
/// trimmed .NET application has reflection-based serialization switched off
/// outright - <c>JsonSerializer.Deserialize&lt;BoardDto&gt;</c> throws
/// "Reflection-based serialization has been disabled for this application" on
/// the first call, on the operator's machine and nowhere before it. This is
/// what makes the shipped binary work at all.</para>
///
/// <para>A type that is not listed here throws where it is used rather than
/// coming back with its fields empty, which is the right way round: a wire
/// record nobody registered is a bug in this file, and it says so at the call
/// that needed it.</para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]

// What is read.
[JsonSerializable(typeof(BoardDto))]
[JsonSerializable(typeof(IssueDto))]
[JsonSerializable(typeof(WorkDto))]
[JsonSerializable(typeof(CommentDto))]
[JsonSerializable(typeof(WorkLogEntryDto))]
[JsonSerializable(typeof(ClaimTakenDto))]
[JsonSerializable(typeof(ClaimPreemptedDto))]
[JsonSerializable(typeof(RunnerInstructionDto))]
[JsonSerializable(typeof(List<RunnerDto>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(CredentialDto))]
[JsonSerializable(typeof(List<StatusDto>))]
[JsonSerializable(typeof(List<CommentDto>))]
[JsonSerializable(typeof(List<QuestionDto>))]
[JsonSerializable(typeof(List<IssueCardDto>))]
[JsonSerializable(typeof(List<QueueEntryDto>))]
[JsonSerializable(typeof(List<ReviewCheckDto>))]
[JsonSerializable(typeof(MergeCheckDto))]
[JsonSerializable(typeof(LandingDto))]
[JsonSerializable(typeof(BuildCheckDto))]
[JsonSerializable(typeof(TrunkBuildDto))]
[JsonSerializable(typeof(List<TrunkBuildDto>))]
[JsonSerializable(typeof(ProjectRepositoryDto))]
[JsonSerializable(typeof(List<ProjectRepositoryDto>))]
[JsonSerializable(typeof(PlaybookFilesDto))]
[JsonSerializable(typeof(PlaybookFileDto))]
[JsonSerializable(typeof(ChangelogDto))]
[JsonSerializable(typeof(ChangelogDayDto))]
[JsonSerializable(typeof(ChangelogEntryDto))]
[JsonSerializable(typeof(ChangelogMonthDto))]
[JsonSerializable(typeof(List<ProjectDto>))]

// What is written.
[JsonSerializable(typeof(IssueCreateRequest))]
[JsonSerializable(typeof(CommentCreateRequest))]
[JsonSerializable(typeof(MessageDeliverRequest))]
[JsonSerializable(typeof(IssueMoveRequest))]
[JsonSerializable(typeof(IssuePatchRequest))]
[JsonSerializable(typeof(IssueDependencyRequest))]
[JsonSerializable(typeof(OnePrRequest))]
[JsonSerializable(typeof(ClaimGraceRequest))]
[JsonSerializable(typeof(ReleaseNoteRequest))]
[JsonSerializable(typeof(WorkLogEntryRequest))]
[JsonSerializable(typeof(ClaimRequest))]
[JsonSerializable(typeof(ClaimHeartbeatRequest))]
[JsonSerializable(typeof(RunnerHeartbeatRequest))]
[JsonSerializable(typeof(MergeCheckRequest))]
[JsonSerializable(typeof(LandingWriteRequest))]
[JsonSerializable(typeof(BuildCheckRequest))]
[JsonSerializable(typeof(StallRequest))]
[JsonSerializable(typeof(VerdictCreateRequest))]
[JsonSerializable(typeof(TrunkBuildRequest))]
[JsonSerializable(typeof(PullRequestMergedRequest))]
[JsonSerializable(typeof(PullRequestClosedRequest))]
[JsonSerializable(typeof(PullRequestStateRequest))]
[JsonSerializable(typeof(BroughtCurrentRequest))]
[JsonSerializable(typeof(PullRequestPushbackRequest))]
[JsonSerializable(typeof(NightState))]
[JsonSerializable(typeof(List<ProjectRepositoryWriteRequest>))]
[JsonSerializable(typeof(PlaybookFileRequest))]

// What a hook prints for the session that ran it.
[JsonSerializable(typeof(PostToolUseOutput))]
[JsonSerializable(typeof(StopBlock))]
[JsonSerializable(typeof(ClampFact))]
internal sealed partial class HatchJson : JsonSerializerContext;
