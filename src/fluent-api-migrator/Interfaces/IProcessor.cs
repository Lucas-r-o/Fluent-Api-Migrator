using fluent_api_migrator.Models;

namespace fluent_api_migrator.Interfaces
{
    public interface IProcessor
    {
        void Process(ProcessorContext context);
    }
}
