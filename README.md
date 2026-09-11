# StanTrack

StanTrack is a web app for keeping up with the celebrities you follow. Browse a directory of actors, musicians, and K-pop acts, follow the ones you care about, and see their upcoming events in one dashboard: concerts, movie and TV releases, album drops, and birthdays.

## What it does

- **Celebrity directory** — browse and search hundreds of celebrities across film, music, and K-pop, with photos and bios.
- **Follow feed** — a personal dashboard showing upcoming events for the celebrities you follow.
- **Birthdays included** — birthdays are tracked alongside concerts and releases so you never miss one.
- **Public events page** — see what's coming up across every tracked celebrity, no account needed.
- **User accounts** — register, log in, reset your password via email.
- **API access** — programmatic access to celebrity and event data, secured with API keys.

## Where the data comes from

- **Ticketmaster** for concerts and live events
- **TMDb** (The Movie Database) for film and TV appearances
- **MusicBrainz** for upcoming music releases
- **Wikidata** for celebrity profiles and photos

## Tech stack

- **ASP.NET Core MVC (.NET 10)** — web framework
- **Entity Framework Core** — data access
- **SQL Server (LocalDB)** — database
- **ASP.NET Core Identity** — user accounts and login
- **Razor views** — server-rendered pages
- **Background service** — scheduled event syncing
- **MailKit + Brevo** — transactional email (password resets)
- **Scalar / OpenAPI** — API documentation
