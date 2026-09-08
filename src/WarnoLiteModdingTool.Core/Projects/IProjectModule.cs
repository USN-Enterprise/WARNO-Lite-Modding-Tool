namespace WarnoLiteModdingTool.Core.Projects;

public interface IProjectModule
{
    string Key { get; }

    string DisplayName { get; }

    ModuleCapability Probe(ModProjectLayout layout);
}

