using LegacyAppDemo.Console;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;

namespace Change.Namespace.Generated
{
  internal class UserConfiguration : EntityTypeConfiguration<User>
  {
    public UserConfiguration()
    {
      ToTable("User", "dbo");
      HasKey(e => e.Id);
      Property(e => e.Id).HasColumnName("Id").HasColumnType("int").IsRequired().HasDatabaseGeneratedOption(DatabaseGeneratedOption.Identity);
      Property(e => e.Name).HasColumnName("Name").HasColumnType("nvarchar").IsRequired().HasMaxLength(50);
      Property(e => e.Surname).HasColumnName("Surname").HasColumnType("nvarchar").IsRequired().HasMaxLength(50);
      Property(e => e.Active).HasColumnName("Active").HasColumnType("bit").IsRequired();

    }
  }
}
