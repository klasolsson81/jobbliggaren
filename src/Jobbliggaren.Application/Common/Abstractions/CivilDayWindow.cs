namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>A Swedish calendar date and its half-open UTC range.</summary>
public readonly record struct CivilDayWindow(DateOnly Day, DateTimeOffset Start, DateTimeOffset End);
