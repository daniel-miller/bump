-- 050: Announcements (planned maintenance, info banners). owner_key NULL =
-- cross-owner / global announcement.

CREATE TABLE IF NOT EXISTS announcement
(
    announcement_key       serial        PRIMARY KEY,
    announcement_title     varchar(200)  NOT NULL,
    announcement_type      varchar(16)   NOT NULL DEFAULT 'info'
        CHECK (announcement_type IN ('info','warning','maintenance')),
    announcement_content   text          NOT NULL,

    owner_key              int           REFERENCES owner(owner_key) ON DELETE CASCADE,

    notify_subscribers     boolean       NOT NULL DEFAULT true,

    created_by             uuid          REFERENCES account(account_id) ON DELETE SET NULL,
    created_at             timestamptz   NOT NULL DEFAULT now(),
    updated_at             timestamptz,

    publish_at             timestamptz   NOT NULL,
    auto_hide_at           timestamptz,
    dispatched_at          timestamptz
);

CREATE INDEX IF NOT EXISTS ix_announcement_owner      ON announcement (owner_key);
CREATE INDEX IF NOT EXISTS ix_announcement_publish_at ON announcement (publish_at);
CREATE INDEX IF NOT EXISTS ix_announcement_pending    ON announcement (publish_at) WHERE dispatched_at IS NULL;
