-- 006: Idempotency-key cache
--
-- Stores the first successful response for each (api_key, idempotency_key)
-- pair so retries can replay it instead of re-running the handler.
--
-- `bearer_token_hash` is SHA-256 of the bearer token — we never store the token
-- itself. `request_fingerprint` is SHA-256 of the request body, used to
-- detect clients that reuse a key with a different payload.

CREATE TABLE IF NOT EXISTS idempotency
(
    idempotency_key         bigserial       PRIMARY KEY,
    idempotency_code        varchar(255)    NOT NULL,

    bearer_token_hash              bytea           NOT NULL,

    request_fingerprint     bytea           NOT NULL,

    response_status         int             NOT NULL,
    response_content_type   varchar(100),
    response_body           bytea           NOT NULL,

    created_at              timestamptz     NOT NULL DEFAULT now(),
    expires_at              timestamptz     NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_idempotency_lookup
  ON idempotency (bearer_token_hash, idempotency_code);

CREATE INDEX IF NOT EXISTS ix_idempotency_expires
  ON idempotency (expires_at);
