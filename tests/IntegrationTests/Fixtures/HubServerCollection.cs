namespace IntegrationTests.Fixtures;

[CollectionDefinition(Name)]
public sealed class HubServerCollection : ICollectionFixture<HubServerFixture>
{
    #region Fields

    public const string Name = "HubServer";

    #endregion
}
