using fluent_api_migrator.Interfaces;
using fluent_api_migrator.Models;
using fluent_api_migrator.Processors;
using System;
using System.Collections.Generic;
using System.Configuration;

namespace fluent_api_migrator.Console
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var context = new ProcessorContext()
                {
                    EdmxFilePath = ConfigurationManager.AppSettings["edmxFilePath"],
                    OutputDirectory = ConfigurationManager.AppSettings["outputDirectory"],
                };

                var processors = new List<IProcessor> { new EdmxFileProcessor(), new FluentApiProcessor() };
                foreach (var processor in processors)
                {
                    processor.Process(context);
                }
            }
            catch (Exception ex)
            {

            }
        }
    }
}
