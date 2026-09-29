using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Context;

internal static class HouseholdModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureMember(modelBuilder);
        ConfigureItem(modelBuilder);
        ConfigureCompletion(modelBuilder);
        ConfigureConsumable(modelBuilder);
        ConfigureTemplate(modelBuilder);
        ConfigureNotificationSetting(modelBuilder);
        ConfigureReminderLog(modelBuilder);
        ConfigureConsumableReminder(modelBuilder);
    }

    private static void ConfigureMember(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdMember>();
        entity.Property(m => m.Role).HasConversion<string>().HasMaxLength(32);

        entity.HasIndex(m => m.UserId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        entity.HasIndex(m => m.HouseholdId);

        entity.HasOne(m => m.Household)
            .WithMany(h => h.Members)
            .HasForeignKey(m => m.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureItem(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdItem>();
        entity.Property(i => i.Category).HasConversion<string>().HasMaxLength(32);
        entity.Property(i => i.ItemType).HasConversion<string>().HasMaxLength(32);
        entity.Property(i => i.CycleUnit).HasConversion<string>().HasMaxLength(32);
        // 哨兵设为 -1，这样提前提醒天数 0（到期当天才提醒）不会被当成“未赋值”而落回默认 7。
        entity.Property(i => i.LeadDays).HasDefaultValue(7).HasSentinel(-1);

        entity.HasIndex(i => new { i.HouseholdId, i.IsPaused, i.NextDueDate });
        entity.HasIndex(i => new { i.HouseholdId, i.Category });

        entity.HasOne(i => i.Household)
            .WithMany(h => h.Items)
            .HasForeignKey(i => i.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(i => i.Assignee)
            .WithMany()
            .HasForeignKey(i => i.AssigneeMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(i => i.Consumable)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.ConsumableId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCompletion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdCompletionRecord>();
        entity.Property(r => r.Cost).HasPrecision(18, 2);
        entity.Property(r => r.IdempotencyKey).HasMaxLength(100);
        entity.Property(r => r.RequestBodyHash).HasMaxLength(64);
        entity.Property(r => r.SubmissionFingerprint).HasMaxLength(64);
        entity.HasIndex(r => new { r.HouseholdItemId, r.CompletedOn });
        entity.HasIndex(r => new { r.HouseholdItemId, r.IdempotencyUserId, r.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");

        entity.HasOne(r => r.Item)
            .WithMany(i => i.Completions)
            .HasForeignKey(r => r.HouseholdItemId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.CompletedBy)
            .WithMany()
            .HasForeignKey(r => r.CompletedByMemberId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureConsumable(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdConsumable>();
        entity.Property(c => c.RestockThreshold).HasDefaultValue(1).HasSentinel(-1);
        entity.HasIndex(c => c.HouseholdId);

        entity.HasOne(c => c.Household)
            .WithMany(h => h.Consumables)
            .HasForeignKey(c => c.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTemplate(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdItemTemplate>();
        entity.Property(t => t.Category).HasConversion<string>().HasMaxLength(32);
        entity.Property(t => t.ItemType).HasConversion<string>().HasMaxLength(32);
        entity.Property(t => t.CycleUnit).HasConversion<string>().HasMaxLength(32);
        entity.HasIndex(t => t.SortOrder);
        entity.HasData(HouseholdItemTemplateSeed.All);
    }

    private static void ConfigureNotificationSetting(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdNotificationSetting>();
        entity.Property(s => s.LeadChannel).HasConversion<string>().HasMaxLength(16).HasDefaultValue(HouseholdNotificationChannel.Email);
        entity.Property(s => s.DueChannel).HasConversion<string>().HasMaxLength(16).HasDefaultValue(HouseholdNotificationChannel.Bark);
        entity.Property(s => s.BarkEnabled).HasDefaultValue(true);
        entity.Property(s => s.EmailEnabled).HasDefaultValue(true);
        entity.Property(s => s.PushHour).HasDefaultValue(9);
        entity.Property(s => s.PushMinute).HasDefaultValue(0);
        entity.Property(s => s.OverdueIntervalDays).HasDefaultValue(3);
        entity.HasIndex(s => s.MemberId).IsUnique().HasFilter("[IsDeleted] = 0");

        entity.HasOne(s => s.Member)
            .WithMany()
            .HasForeignKey(s => s.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureReminderLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdReminderLog>();
        entity.Property(r => r.Channel).HasConversion<string>().HasMaxLength(16);
        entity.HasIndex(r => new { r.HouseholdItemId, r.MemberId, r.ReminderDate, r.Channel })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        entity.HasIndex(r => new { r.MemberId, r.ReminderDate });

        entity.HasOne(r => r.Item)
            .WithMany()
            .HasForeignKey(r => r.HouseholdItemId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Member)
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureConsumableReminder(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HouseholdConsumableReminder>();
        entity.Property(r => r.Channel).HasConversion<string>().HasMaxLength(16);
        entity.HasIndex(r => new { r.ConsumableId, r.MemberId, r.Channel })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        entity.HasOne(r => r.Consumable)
            .WithMany()
            .HasForeignKey(r => r.ConsumableId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Member)
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
