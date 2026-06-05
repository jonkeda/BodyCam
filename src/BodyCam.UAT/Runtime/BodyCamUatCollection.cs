namespace BodyCam.UAT.Runtime;

[CollectionDefinition(CollectionName)]
public sealed class BodyCamUatCollection : ICollectionFixture<BodyCamUatFixture>
{
    public const string CollectionName = "BodyCam UAT";
}
