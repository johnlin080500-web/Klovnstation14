using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Trading;

/// <summary>
/// Public corporate announcement issued at rule start. The exchange queues the price
/// shock separately so it survives the short station event's end.
/// </summary>
[RegisterComponent, EntityCategory("GameRules")]
public sealed partial class TradingCorporateNewsRuleComponent : Component
{
    [DataField(required: true)]
    public string Symbol = string.Empty;
    [DataField]
    public float MinPercent = 1f;
    [DataField]
    public float MaxPercent = 10f;
    [DataField]
    public int MinDelaySeconds = 60;
    [DataField]
    public int MaxDelaySeconds = 900;
    [DataField]
    public string Title = "Corporate update";
    [DataField]
    public string Body = "New developments may change investor expectations.";
}

public sealed partial class TradingCorporateNewsRuleSystem : StationEventSystem<TradingCorporateNewsRuleComponent>
{
    [Dependency] private TradingMarketSystem _market = default!;

    protected override void Started(
        EntityUid uid,
        TradingCorporateNewsRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        _market.ScheduleCorporateNews(
            component.Symbol,
            component.Title,
            component.Body,
            component.MinPercent,
            component.MaxPercent,
            component.MinDelaySeconds,
            component.MaxDelaySeconds);
    }
}
