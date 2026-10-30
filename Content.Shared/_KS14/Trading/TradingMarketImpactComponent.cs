using Content.Shared.GameTicking.Components;

namespace Content.Shared._KS14.Trading;

/// <summary>
/// Attach to publicly observable station event rules; applied once on rule start,
/// including rules launched during a round. Avoid secret antagonist rules to prevent leaks.
/// </summary>
[RegisterComponent, EntityCategory("GameRules")]
public sealed partial class TradingMarketImpactComponent : Component
{
    [DataField(required: true)]
    public string Symbol = string.Empty;

    /// <summary>Relative share price change in percent, e.g. -20 or 15.</summary>
    [DataField]
    public float Percent;

    [DataField]
    public string Headline = string.Empty;

    /// <summary>Server-only guard against repeated event delivery.</summary>
    public bool Triggered;
}
