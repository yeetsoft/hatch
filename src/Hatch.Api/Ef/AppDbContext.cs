using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Ef;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<EfSiteSetting> SiteSettings => Set<EfSiteSetting>();
    public DbSet<EfOAuthState> OAuthStates => Set<EfOAuthState>();

    public DbSet<EfAuthGrant> AuthGrants => Set<EfAuthGrant>();
    public DbSet<EfAuthInvite> AuthInvites => Set<EfAuthInvite>();
    public DbSet<EfApiKey> ApiKeys => Set<EfApiKey>();

    public DbSet<EfExternalIdentity> ExternalIdentities => Set<EfExternalIdentity>();
    public DbSet<EfPerson> People => Set<EfPerson>();
    public DbSet<EfPersonPhoto> PersonPhotos => Set<EfPersonPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfSiteSetting>();

        modelBuilder.Entity<EfOAuthState>();

        // Auth lives in the core context and the public schema on purpose: it
        // is infrastructure every module sits behind, and a Modules/ schema
        // would make every module depend on one module (Modules/README.md).
        // An invite still has no relationships - a grant outlives the invite
        // that made it, which is why both EfAuthInvite.RedeemedGrantId and its
        // PersonId are bare Guids rather than foreign keys.
        //
        // A grant now has exactly one: its optional owner. Cascade, and it is
        // the single most important word in this file - deleting a person
        // revokes their devices. That is the point of deleting them: an
        // ownerless grant is refused by the role gate anyway, so the alternative
        // (SetNull) would leave credentials that reach nothing and that nobody
        // is looking at.
        modelBuilder.Entity<EfAuthGrant>()
            .HasOne(g => g.Person)
            .WithMany(p => p.Grants)
            .HasForeignKey(g => g.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EfAuthInvite>();

        // A key now has exactly one relationship: who it belongs to, for a
        // --mine pass to read. SetNull rather than Cascade, unlike a grant -
        // a key is a program the operator handed a credential to, and
        // deleting its owner does not mean the program stops running; it
        // means the program no longer has anybody's tickets to claim, which
        // is precisely "owned by nobody".
        modelBuilder.Entity<EfApiKey>()
            .HasOne(k => k.Owner)
            .WithMany()
            .HasForeignKey(k => k.OwnerPersonId)
            .OnDelete(DeleteBehavior.SetNull);

        // People sit beside auth for the same reason auth sits here: they are
        // infrastructure the whole install shares rather than one family app's
        // data, and a module schema would make every module depend on one
        // module. A person is not an account - see EfPerson.
        //
        // One-to-one, declared rather than inferred: PersonId is both the key
        // and the foreign key, so convention would otherwise invent a second
        // shadow column. Cascade is the part that matters - a deleted person
        // takes their photo with it, and an orphaned two-megabyte blob is not
        // something anything else in the app would ever notice.
        modelBuilder.Entity<EfPerson>();

        // Cascade: an identity proves who somebody is and means nothing without
        // them - and the same call as a grant's, which ends with its owner.
        modelBuilder.Entity<EfExternalIdentity>()
            .HasOne(i => i.Person)
            .WithMany(p => p.Identities)
            .HasForeignKey(i => i.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Partial, because an unclaimed identity has no Subject and NULLs are
        // not a key. The second index is what stops one address being
        // pre-approved twice; the address is stored lower-cased while unclaimed,
        // so a plain index is a case-insensitive one.
        modelBuilder.Entity<EfExternalIdentity>()
            .HasIndex(i => new { i.Provider, i.Subject })
            .IsUnique()
            .HasFilter("\"Subject\" IS NOT NULL");
        modelBuilder.Entity<EfExternalIdentity>()
            .HasIndex(i => new { i.Provider, i.Email })
            .IsUnique()
            .HasFilter("\"Subject\" IS NULL")
            .HasDatabaseName("IX_ExternalIdentities_Provider_Email_Unclaimed");

        modelBuilder.Entity<EfPersonPhoto>()
            .HasOne(p => p.Person)
            .WithOne(p => p.Photo)
            .HasForeignKey<EfPersonPhoto>(p => p.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}
