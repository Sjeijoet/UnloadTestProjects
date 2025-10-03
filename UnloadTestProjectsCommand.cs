using System.Diagnostics;
using Microsoft;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.VSSdkCompatibility;
using Microsoft.VisualStudio.ProjectSystem.Query;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace UnloadTestProjects
{
    /// <summary>
    /// Command1 handler.
    /// </summary>
    [VisualStudioContribution]
    internal class UnloadTestProjectsCommand : Command
    {
        // Extract from vsshlids.h
        private const string guidSHLMainMenu = "D309F791-903F-11D0-9EFC-00A0C911004F";
        private const uint IDG_VS_CTXT_SOLUTION_EXPLORE = 0x0265;

        private readonly TraceSource logger;
        private readonly AsyncServiceProviderInjection<SVsSolution, IVsSolution> vsSolution;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnloadTestProjectsCommand"/> class.
        /// </summary>
        /// <param name="traceSource">Trace source instance to utilize.</param>
        public UnloadTestProjectsCommand(
            AsyncServiceProviderInjection<SVsSolution, IVsSolution> vsSolution,
            TraceSource traceSource)
        {
            this.vsSolution = vsSolution;
            this.logger = Requires.NotNull(traceSource, nameof(traceSource));
        }

        /// <inheritdoc />
        public override CommandConfiguration CommandConfiguration => new("%UnloadTestProjects.UnloadTestProjectsCommand.DisplayName%")
        {
            VisibleWhen = ActivationConstraint.SolutionHasProjectCapability(ProjectCapability.TestContainer),
            Icon = new(ImageMoniker.KnownValues.RemoveTestGroup, IconSettings.IconAndText),
            Placements =
            [
                CommandPlacement.VsctParent(
                    guid: new Guid(guidSHLMainMenu),
                    id: IDG_VS_CTXT_SOLUTION_EXPLORE,
                    priority: 0x0225)
            ],
        };

        /// <inheritdoc />
        public override Task InitializeAsync(CancellationToken cancellationToken)
        {
            return base.InitializeAsync(cancellationToken);
        }

        /// <inheritdoc />
        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                var projects = await this.Extensibility
                    .Workspaces()
                    .QueryProjectsAsync(projects => projects
                        .With(p => p.Guid)
                        .With(p => p.Capabilities)
                        , cancellationToken);

                var testProjects = projects.Where(p => p.Capabilities.Contains(ProjectCapability.TestContainer.ToString()));

                if (!testProjects.Any())
                {
                    await this.Extensibility.Shell().ShowPromptAsync("Solution contains no test projects.", PromptOptions.OK, cancellationToken);
                    return;
                }

                var projectGuids = testProjects.Select(i => i.Guid).ToArray();
                await this.UnloadProjectsAsync(projectGuids, cancellationToken);
            }
            catch (Exception ex)
            {
                await this.Extensibility.Shell().ShowPromptAsync("UnloadTestProjects: " + ex.ToString(), PromptOptions.OK, cancellationToken);
            }
        }

        private async Task UnloadProjectsAsync(Guid[] projectGuids, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var solution = await this.vsSolution.GetServiceAsync();

            (solution as IVsSolution8)?.BatchProjectAction((uint)__VSBatchProjectAction.BPA_UNLOAD, (uint)(__VSBatchProjectActionFlags.BPAF_CLOSE_FILES | __VSBatchProjectActionFlags.BPAF_PROMPT_SAVE), (uint)projectGuids.Length, projectGuids, out var actionContext);
        }
    }
}
