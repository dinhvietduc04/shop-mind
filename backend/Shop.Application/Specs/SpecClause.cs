namespace Shop.Application.Specs;

/// <summary>
/// A3 / §B5+ 4: one parsed, validated filter clause. <see cref="Value"/> is
/// always one of <see cref="double"/> (range), <see cref="string"/> (enum),
/// <see cref="bool"/> (bool), or <see cref="IReadOnlyList{T}"/> of strings
/// (<see cref="SpecOp.In"/>).
/// </summary>
public sealed record SpecClause(string Key, SpecOp Op, object Value);
