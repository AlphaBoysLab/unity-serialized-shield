using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace UnitySerializedShield.VisualStudio
{
    /// <summary>
    /// Extension entrypoint for the VisualStudio.Extensibility extension.
    /// </summary>
    [VisualStudioContribution]
    internal class ExtensionEntrypoint : Extension
    {
        /// <inheritdoc/>
        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            Metadata = new(
                    id: "UnitySerializedShield.VisualStudio.80229c4b-82bf-41ef-b409-98eaaaff3e1c",
                    version: this.ExtensionAssemblyVersion,
                    publisherName: "AlphaBoysLab",
                    displayName: "UnitySerializedShield",
                    description: "Protects Unity serialized fields when renamed in Visual Studio.")
            {
                Icon = "Images/icon.png",
                Tags = ["Unity", "C#", "SerializeField", "FormerlySerializedAs", "Refactoring"],
                Preview = false,
            },
        };

        /// <inheritdoc />
        protected override void InitializeServices(IServiceCollection serviceCollection)
        {
            base.InitializeServices(serviceCollection);

            // You can configure dependency injection here by adding services to the serviceCollection.
        }
    }
}
