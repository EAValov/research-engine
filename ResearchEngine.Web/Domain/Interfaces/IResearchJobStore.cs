
namespace ResearchEngine.Domain;

public interface IResearchJobStore :
    IResearchJobRepository,
    IResearchEventRepository,
    IResearchSourceRepository,
    IResearchLearningRepository,
    IResearchLearningGroupRepository,
    IResearchSynthesisRepository,
    IResearchSynthesisOverridesRepository
{
}
