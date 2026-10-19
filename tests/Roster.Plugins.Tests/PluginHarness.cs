using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;

namespace Roster.Plugins.Tests
{
    /// <summary>
    /// Builds the service provider Dataverse hands a plugin, with a substitute
    /// organisation service so tests control what queries return.
    /// </summary>
    internal sealed class PluginHarness
    {
        public IPluginExecutionContext Context { get; } = Substitute.For<IPluginExecutionContext>();
        public IOrganizationService Service { get; } = Substitute.For<IOrganizationService>();
        public QueryExpression LastQuery { get; private set; }

        private readonly IServiceProvider _provider = Substitute.For<IServiceProvider>();

        public PluginHarness(string message, Entity target, Entity preImage = null)
        {
            Context.MessageName.Returns(message);
            Context.InputParameters.Returns(new ParameterCollection { { "Target", target } });

            var images = new EntityImageCollection();
            if (preImage != null)
            {
                images.Add(ValidateShiftPlugin.PreImageName, preImage);
            }
            Context.PreEntityImages.Returns(images);

            var factory = Substitute.For<IOrganizationServiceFactory>();
            factory.CreateOrganizationService(Arg.Any<Guid?>()).Returns(Service);

            _provider.GetService(typeof(IPluginExecutionContext)).Returns(Context);
            _provider.GetService(typeof(ITracingService)).Returns(Substitute.For<ITracingService>());
            _provider.GetService(typeof(IOrganizationServiceFactory)).Returns(factory);

            ExistingShifts();
        }

        /// <summary>Sets the rows the overlap query will find.</summary>
        public void ExistingShifts(params Entity[] shifts)
        {
            Service.RetrieveMultiple(Arg.Any<QueryBase>()).Returns(call =>
            {
                LastQuery = call.Arg<QueryBase>() as QueryExpression;
                return new EntityCollection(shifts);
            });
        }

        public void Run() => new ValidateShiftPlugin().Execute(_provider);
    }
}
