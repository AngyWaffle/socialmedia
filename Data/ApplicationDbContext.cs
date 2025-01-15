using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Posts> Posts { get; set; }
    public DbSet<Comments> Comments { get; set; }
    public DbSet<Likes> Likes { get; set; }
    public DbSet<Followers> Followers { get; set; }
    public DbSet<Notifications> Notifications { get; set; }
    public DbSet<UserInteraction> UserInteraction { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Create unique index for Username
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // Create unique index for Mail
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Mail)
            .IsUnique();

        // Configure JSON serialization for Users.PreferredTags
        modelBuilder.Entity<User>()
            .Property(u => u.PreferredTags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null)
            );
    }
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Mail { get; set; }
    public string? Bio { get; set; }
    public string? ProfileImageUrl { get; set; }
    public List<string>? PreferredTags { get; set; } // e.g., ["science", "music"]
    public List<int>? FollowingUsers { get; set; } // List of user IDs the person follows
    public List<int>? LikedPosts { get; set; } // List of liked post IDs
}

public class Posts
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Content {  get; set; }
    public string? ImageUrl { get; set; }
    public string Username { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string>? Tags { get; set; }
}

public class Comments
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int UserId { get; set; }
    public string CommentText { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Likes
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int UserId { get; set; }
}

public class Followers
{
    public int Id { get; set; }
    public int FollowerId { get; set; }
    public int FollowedId { get; set; }
}

public class Notifications
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Type { get; set; }
    public int? RelatedUserId { get; set; }
    public int? RelatedPostId { get; set; }
    public string Message { get; set; }
    public string CreatedAt { get; set; }
}

public class UserInteraction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int PostId { get; set; }
    public DateTime InteractionTime { get; set; }
    public string InteractionType { get; set; }
}
