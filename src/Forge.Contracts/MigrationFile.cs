namespace Forge.Contracts;

// One migration of an install plan, ordered by Number (SQL.md, Migrations).
public sealed record MigrationFile(int Number, string FileName, string Path);
