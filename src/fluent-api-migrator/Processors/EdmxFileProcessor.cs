using fluent_api_migrator.Exceptions;
using fluent_api_migrator.Interfaces;
using fluent_api_migrator.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core.Mapping;
using System.Data.Entity.Core.Metadata.Edm;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace fluent_api_migrator.Processors
{
    public class EdmxFileProcessor : IProcessor
    {
        public void Process(ProcessorContext context)
        {
            if (!File.Exists(context.EdmxFilePath)) throw new ArgumentException("Specified file is not exist");

            if (Path.GetExtension(context.EdmxFilePath).ToLower() != ".edmx")
                throw new ArgumentException("File is not an edmx file.");

            context.EdmxParseResult = LoadAndParseEdmxFile(context.EdmxFilePath);
        }

        private EdmxParseResult LoadAndParseEdmxFile(string filePath)
        {
            var xdoc = XDocument.Load(filePath);

            var ssdlSection = xdoc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "StorageModels")
                ?.Descendants()
                ?.FirstOrDefault(e => e.Name.LocalName == "Schema") ?? throw new EdmxFileException("Could not load storage models schema section");

            var csdlSection = xdoc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "ConceptualModels")
                ?.Descendants()
                ?.FirstOrDefault(e => e.Name.LocalName == "Schema") ?? throw new EdmxFileException("Could not load conceptual models schema section");

            var mappingsSection = xdoc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Mappings")
                ?.Descendants()
                ?.FirstOrDefault(e => e.Name.LocalName == "Mapping") ?? throw new EdmxFileException("Could not load mapping section");


            StorageMappingItemCollection mappingsCollection;
            using (var csdlReader = csdlSection.CreateReader())
            {
                using (var ssdlReader = ssdlSection.CreateReader())
                {
                    using (var mappingsReader = mappingsSection.CreateReader())
                    {
                        var ssdlCollection = StoreItemCollection.Create(new[] { ssdlReader }, null, null, out var ssdlErrors);
                        if (ssdlErrors?.Any() ?? false)
                            throw new EdmxFileException("Errors occured when parsing ssdl section: " + string.Join(", ", ssdlErrors));

                        var csdlCollection = EdmItemCollection.Create(new[] { csdlReader }, null, out var csdlErrors);
                        if (csdlErrors?.Any() ?? false)
                            throw new EdmxFileException("Errors occured when parsing csdl section: " + string.Join(", ", csdlErrors));

                        mappingsCollection = StorageMappingItemCollection.Create(csdlCollection, ssdlCollection, new[] { mappingsReader }, null, out var mappingsErrors);
                        if (mappingsErrors?.Any() ?? false)
                            throw new EdmxFileException("Errors occured when parsing mappings section: " + string.Join(", ", mappingsErrors));
                    }
                }
            }

            return ParseMappingsCollection(mappingsCollection);
        }

        private EdmxParseResult ParseMappingsCollection(StorageMappingItemCollection mappingsCollection)
        {
            var entitySetMappings = mappingsCollection.GetItems<EntityContainerMapping>().SelectMany(m => m.EntitySetMappings).ToList();
            var relationshipDescriptions = GetRelationshipDescriptions(entitySetMappings);
            var (tableColumnsDescriptions, commonEntityInfos) = GetTableColumnDescriptions(entitySetMappings);
            return new EdmxParseResult
            {
                EntitySetMappings = entitySetMappings,
                RelationshipDescriptions = relationshipDescriptions,
                TableColumnsDescriptions = tableColumnsDescriptions,
                CommonEntityInfos = commonEntityInfos,
            };
        }

        private (Dictionary<string, List<TableColumnDescription>>, Dictionary<string, CommonEntityInfo>) GetTableColumnDescriptions(List<EntitySetMapping> entitySetMappings)
        {
            var tableColumnDescription = new Dictionary<string, List<TableColumnDescription>>();
            var commonEntityInfos = new Dictionary<string, CommonEntityInfo>();

            foreach (var entitySetMapping in entitySetMappings)
            {
                var entityTypeMapping = entitySetMapping.EntityTypeMappings.First();
                var fragment = entityTypeMapping.Fragments.First(); 
                var storageEntityType = fragment.StoreEntitySet.ElementType; 
                var scalarPropertyMappings = fragment.PropertyMappings.OfType<ScalarPropertyMapping>(); 
                var conceptualEntityType = entitySetMapping.EntitySet.ElementType;
                var storageKeyProperties = storageEntityType.KeyProperties; 

                commonEntityInfos[conceptualEntityType.Name] = new CommonEntityInfo
                {
                    PrimaryKeys = storageKeyProperties.Select(p => p.Name).ToList(),
                    Table = fragment.StoreEntitySet.Table ?? fragment.StoreEntitySet.Name,
                    Schema = fragment.StoreEntitySet.Schema,
                };

                tableColumnDescription[conceptualEntityType.Name] = storageEntityType.Properties.Select((storageProperty, index) =>
                {
                    var mappedPropertyMapping = scalarPropertyMappings
                    .FirstOrDefault(mapping => mapping.Column.Name.Equals(storageProperty.Name))?.Property; 

                    if (mappedPropertyMapping is null) return null; 

                    return new TableColumnDescription
                    {
                        IsPrimaryKey = storageKeyProperties.Any(keyProp => keyProp.Name.Equals(storageProperty.Name, StringComparison.OrdinalIgnoreCase)),
                        IsFixedLength = storageProperty.IsFixedLength,
                        IsComputed = storageProperty.IsStoreGeneratedComputed,
                        IsIdentity = storageProperty.IsStoreGeneratedIdentity,
                        IsNullable = storageProperty.Nullable,
                        MaxLength = storageProperty.MaxLength,
                        EntityName = mappedPropertyMapping.Name,
                        SqlType = storageProperty.TypeName,
                        TableName = storageProperty.Name,
                    };
                }).Where(tableColumnDesc => tableColumnDesc != null).ToList();
            }

            return (tableColumnDescription, commonEntityInfos);
        }

        private Dictionary<string, List<RelationshipDescription>> GetRelationshipDescriptions(List<EntitySetMapping> entitySetMappings)
        {
            var foreignKeysInfo = ExtractForeignKeysInfo(entitySetMappings); 
            var relationshipData = new Dictionary<string, List<RelationshipDescription>>();

            foreach (var entitySetMapping in entitySetMappings)
            {
                var entitySetName = entitySetMapping.EntitySet.ElementType.Name;
                relationshipData[entitySetName] = new List<RelationshipDescription>(); 

                var relatedForeignKeys = foreignKeysInfo.Values
                    .Where(fkInfo => (fkInfo.EntitySetNameFrom?.Equals(entitySetName) ?? false) || (fkInfo.EntitySetNameTo?.Equals(entitySetName) ?? false));

                foreach (var foreignKey in relatedForeignKeys)
                {
                    var relationshipSettings = new RelationshipDescription
                    {
                        ForeignKeys = foreignKey.ForeignKeys.ToList(),
                        DeleteBehavior = ((foreignKey.DeleteBehaviorFrom == OperationAction.Cascade || foreignKey.DeleteBehaviorTo == OperationAction.Cascade)
                            ? OperationAction.Cascade
                            : OperationAction.None),
                    };

                    if (entitySetName.Equals(foreignKey.EntitySetNameFrom))
                    {
                        relationshipSettings.From = new RelationshipEntityDescription
                        {
                            EntityName = foreignKey.EntitySetNameFrom,
                            NavigationPropertyName = foreignKey.NavPropertyNameFrom,
                            RelationshipType = foreignKey.RelationshipTo,
                            JoinKeyName = foreignKey.JoinTableKeyFrom
                        };

                        relationshipSettings.To = new RelationshipEntityDescription
                        {
                            EntityName = foreignKey.EntitySetNameTo,
                            NavigationPropertyName = foreignKey.NavPropertyNameTo,
                            RelationshipType = foreignKey.RelationshipFrom,
                            JoinKeyName = foreignKey.JoinTableKeyTo
                        };
                    }
                    else if (entitySetName.Equals(foreignKey.EntitySetNameTo))
                    {
                        relationshipSettings.From = new RelationshipEntityDescription
                        {
                            EntityName = foreignKey.EntitySetNameTo,
                            NavigationPropertyName = foreignKey.NavPropertyNameTo,
                            RelationshipType = foreignKey.RelationshipFrom,
                            JoinKeyName = foreignKey.JoinTableKeyTo
                        };

                        relationshipSettings.To = new RelationshipEntityDescription
                        {
                            EntityName = foreignKey.EntitySetNameFrom,
                            NavigationPropertyName = foreignKey.NavPropertyNameFrom,
                            RelationshipType = foreignKey.RelationshipTo,
                            JoinKeyName = foreignKey.JoinTableKeyFrom
                        };
                    }

                    if (foreignKey.IsManyToMany)
                        relationshipSettings.JoinTableName = foreignKey.JoinTableName;

                    relationshipData[entitySetName].Add(relationshipSettings); 
                }
            }

            return relationshipData;
        }

        private static Dictionary<string, ForeignKeyInfo> ExtractForeignKeysInfo(List<EntitySetMapping> entitySetMappings)
        {
            var associationSetMappings = entitySetMappings.FirstOrDefault()?.ContainerMapping?.AssociationSetMappings.ToList(); 
            var foreignKeysInfo = new Dictionary<string, ForeignKeyInfo>();

            foreach (var entitySetMapping in entitySetMappings)
            {
                foreach (var navProperty in entitySetMapping.EntitySet.ElementType.NavigationProperties)
                {
                    var relationshipType = (AssociationType)navProperty.RelationshipType;

                    if (!foreignKeysInfo.TryGetValue(relationshipType.Name, out var foreignKeyInfo))
                        foreignKeyInfo = new ForeignKeyInfo();

                    if (relationshipType.Constraint is null)
                    {
                        var joinTableMapping = associationSetMappings.First(mapping => mapping.AssociationSet.Name.Equals(relationshipType.Name));
                        AddManyToManyForeignKeyInfo(foreignKeyInfo, entitySetMapping.EntitySet, joinTableMapping, navProperty.Name);
                    }
                    else
                    {
                        AddForeignKeyInfo(foreignKeyInfo, entitySetMapping.EntitySet, navProperty);
                    }

                    foreignKeysInfo[relationshipType.Name] = foreignKeyInfo;
                }
            }

            return foreignKeysInfo;
        }

        private static void AddForeignKeyInfo(ForeignKeyInfo foreignKeyInfo, EntitySet entitySet, NavigationProperty navProperty)
        {
            var constraint = (navProperty.RelationshipType as AssociationType)?.Constraint; 

            foreach (var foreignKeyProperty in constraint.ToProperties.Select(property => property.Name))
                foreignKeyInfo.ForeignKeys.Add(foreignKeyProperty);

            foreignKeyInfo.RelationshipFrom = constraint.FromRole.RelationshipMultiplicity;
            foreignKeyInfo.RelationshipTo = constraint.ToRole.RelationshipMultiplicity;

            var fromEndMemberName = navProperty.FromEndMember.Name;

            if (constraint.FromRole.Name.Equals(fromEndMemberName))
            {
                foreignKeyInfo.NavPropertyNameFrom = navProperty.Name;
                foreignKeyInfo.DeleteBehaviorFrom = constraint.ToRole.DeleteBehavior;
                foreignKeyInfo.EntitySetNameFrom = entitySet.ElementType.Name;
            }
            else
            {
                foreignKeyInfo.NavPropertyNameTo = navProperty.Name;
                foreignKeyInfo.DeleteBehaviorTo = constraint.FromRole.DeleteBehavior;
                foreignKeyInfo.EntitySetNameTo = entitySet.ElementType.Name;
            }
        }

        private static void AddManyToManyForeignKeyInfo(ForeignKeyInfo foreignKeyInfo, EntitySet entitySet, AssociationSetMapping joinTableMapping, string navPropertyName)
        {
            var sourceEndMapping = joinTableMapping.SourceEndMapping;
            var targetEndMapping = joinTableMapping.TargetEndMapping;
            foreignKeyInfo.JoinTableName = joinTableMapping.AssociationSet.Name;

            if (sourceEndMapping.AssociationEnd.Name.Equals(entitySet.ElementType.Name))
            {
                foreignKeyInfo.JoinTableKeyFrom = sourceEndMapping.PropertyMappings.First().Column.Name;
                foreignKeyInfo.RelationshipFrom = sourceEndMapping.AssociationEnd.RelationshipMultiplicity;
                foreignKeyInfo.JoinTableKeyTo = targetEndMapping.PropertyMappings.First().Column.Name;
                foreignKeyInfo.RelationshipTo = targetEndMapping.AssociationEnd.RelationshipMultiplicity;
                foreignKeyInfo.EntitySetNameFrom = entitySet.ElementType.Name;
                foreignKeyInfo.NavPropertyNameFrom = navPropertyName;
            }
            else
            {
                foreignKeyInfo.JoinTableKeyTo = sourceEndMapping.PropertyMappings.First().Column.Name;
                foreignKeyInfo.RelationshipTo = sourceEndMapping.AssociationEnd.RelationshipMultiplicity;
                foreignKeyInfo.JoinTableKeyFrom = targetEndMapping.PropertyMappings.First().Column.Name;
                foreignKeyInfo.RelationshipFrom = targetEndMapping.AssociationEnd.RelationshipMultiplicity;
                foreignKeyInfo.NavPropertyNameTo = navPropertyName;
                foreignKeyInfo.EntitySetNameTo = entitySet.ElementType.Name;
            }
        }
    }
}