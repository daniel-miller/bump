--
-- PostgreSQL database dump
--

\restrict CRxbzsA8jyEYt9KXAEoZwUY9T0PRx7A4Fuu6x3OZDX0x4yUWcqti6mPTWoX7DtM

-- Dumped from database version 18.3
-- Dumped by pg_dump version 18.3

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: bump; Type: DATABASE; Schema: -; Owner: -
--

CREATE DATABASE bump WITH TEMPLATE = template0 ENCODING = 'UTF8' LOCALE_PROVIDER = libc LOCALE = 'English_Canada.1252';


\unrestrict CRxbzsA8jyEYt9KXAEoZwUY9T0PRx7A4Fuu6x3OZDX0x4yUWcqti6mPTWoX7DtM
\connect bump
\restrict CRxbzsA8jyEYt9KXAEoZwUY9T0PRx7A4Fuu6x3OZDX0x4yUWcqti6mPTWoX7DtM

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


SET default_table_access_method = heap;

--
-- Name: _migration_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public._migration_history (
    name text NOT NULL,
    applied_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: account; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.account (
    account_id uuid DEFAULT gen_random_uuid() NOT NULL,
    account_email character varying(254) NOT NULL,
    account_full_name character varying(200) NOT NULL,
    account_timezone character varying(64) DEFAULT 'UTC'::character varying NOT NULL,
    password_hash bytea NOT NULL,
    password_salt bytea NOT NULL,
    password_alg character varying(32) DEFAULT 'argon2id'::character varying NOT NULL,
    totp_secret bytea,
    totp_enabled boolean DEFAULT false NOT NULL,
    account_roles text[] DEFAULT ARRAY['admin'::text] NOT NULL,
    failed_login_count integer DEFAULT 0 NOT NULL,
    locked_until timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: account_email_change; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.account_email_change (
    change_key bigint NOT NULL,
    account_id uuid NOT NULL,
    new_email character varying(254) NOT NULL,
    change_hash bytea NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: account_email_change_change_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.account_email_change_change_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: account_email_change_change_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.account_email_change_change_key_seq OWNED BY public.account_email_change.change_key;


--
-- Name: account_password_change; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.account_password_change (
    change_key bigint NOT NULL,
    change_hash bytea NOT NULL,
    account_id uuid NOT NULL,
    used_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone NOT NULL
);


--
-- Name: account_password_change_change_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.account_password_change_change_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: account_password_change_change_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.account_password_change_change_key_seq OWNED BY public.account_password_change.change_key;


--
-- Name: account_recovery; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.account_recovery (
    recovery_key bigint NOT NULL,
    recovery_hash bytea NOT NULL,
    account_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    used_at timestamp with time zone
);


--
-- Name: account_recovery_recovery_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.account_recovery_recovery_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: account_recovery_recovery_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.account_recovery_recovery_key_seq OWNED BY public.account_recovery.recovery_key;


--
-- Name: account_session; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.account_session (
    session_id uuid DEFAULT gen_random_uuid() NOT NULL,
    account_id uuid NOT NULL,
    user_agent character varying(500),
    user_ip inet,
    bearer_token_hash bytea NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone
);


--
-- Name: announcement; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.announcement (
    announcement_key integer NOT NULL,
    announcement_title character varying(200) NOT NULL,
    announcement_type character varying(16) DEFAULT 'info'::character varying NOT NULL,
    announcement_content text NOT NULL,
    owner_key integer,
    notify_subscribers boolean DEFAULT true NOT NULL,
    created_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    publish_at timestamp with time zone NOT NULL,
    auto_hide_at timestamp with time zone,
    dispatched_at timestamp with time zone,
    CONSTRAINT announcement_announcement_type_check CHECK (((announcement_type)::text = ANY ((ARRAY['info'::character varying, 'warning'::character varying, 'maintenance'::character varying])::text[])))
);


--
-- Name: announcement_announcement_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.announcement_announcement_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: announcement_announcement_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.announcement_announcement_key_seq OWNED BY public.announcement.announcement_key;


--
-- Name: app; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app (
    app_key integer NOT NULL,
    app_handle character varying(50) NOT NULL,
    app_name character varying(200) NOT NULL,
    app_description character varying(500),
    version_major integer DEFAULT 0 NOT NULL,
    version_minor integer DEFAULT 0 NOT NULL,
    version_patch integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: app_app_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.app_app_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: app_app_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.app_app_key_seq OWNED BY public.app.app_key;


--
-- Name: environment; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.environment (
    environment_key integer NOT NULL,
    environment_number smallint,
    environment_handle character varying(60) NOT NULL,
    environment_name character varying(100) NOT NULL,
    environment_description character varying(500),
    environment_aliases text[] DEFAULT ARRAY[]::text[] NOT NULL,
    is_special_purpose boolean DEFAULT false NOT NULL,
    is_derived_from_live boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: environment_environment_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.environment_environment_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: environment_environment_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.environment_environment_key_seq OWNED BY public.environment.environment_key;


--
-- Name: idempotency; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.idempotency (
    idempotency_key bigint NOT NULL,
    idempotency_code character varying(255) NOT NULL,
    bearer_token_hash bytea NOT NULL,
    request_fingerprint bytea NOT NULL,
    response_status integer NOT NULL,
    response_content_type character varying(100),
    response_body bytea NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone NOT NULL
);


--
-- Name: idempotency_idempotency_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.idempotency_idempotency_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: idempotency_idempotency_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.idempotency_idempotency_key_seq OWNED BY public.idempotency.idempotency_key;


--
-- Name: outage; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.outage (
    outage_key integer NOT NULL,
    outage_title character varying(200) NOT NULL,
    outage_status character varying(16) DEFAULT 'investigating'::character varying NOT NULL,
    outage_region character varying(100),
    service_key integer,
    root_cause text,
    auto_created boolean DEFAULT false NOT NULL,
    created_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    resolved_at timestamp with time zone,
    updated_at timestamp with time zone,
    CONSTRAINT outage_outage_status_check CHECK (((outage_status)::text = ANY ((ARRAY['investigating'::character varying, 'identified'::character varying, 'monitoring'::character varying, 'resolved'::character varying])::text[])))
);


--
-- Name: outage_outage_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.outage_outage_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: outage_outage_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.outage_outage_key_seq OWNED BY public.outage.outage_key;


--
-- Name: outage_update; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.outage_update (
    update_key bigint NOT NULL,
    update_message character varying(2000) NOT NULL,
    outage_key integer NOT NULL,
    update_status character varying(16) NOT NULL,
    published boolean DEFAULT true NOT NULL,
    created_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT outage_update_update_status_check CHECK (((update_status)::text = ANY ((ARRAY['investigating'::character varying, 'identified'::character varying, 'monitoring'::character varying, 'resolved'::character varying])::text[])))
);


--
-- Name: outage_update_update_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.outage_update_update_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: outage_update_update_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.outage_update_update_key_seq OWNED BY public.outage_update.update_key;


--
-- Name: owner; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.owner (
    owner_key integer NOT NULL,
    owner_number smallint,
    owner_handle character varying(60) NOT NULL,
    owner_name character varying(100) NOT NULL,
    owner_description character varying(500),
    owner_host character varying(255),
    owner_theme jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: owner_owner_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.owner_owner_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: owner_owner_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.owner_owner_key_seq OWNED BY public.owner.owner_key;


--
-- Name: probe_event; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.probe_event (
    probe_event_key bigint NOT NULL,
    service_key integer NOT NULL,
    checked_at timestamp with time zone DEFAULT now() NOT NULL,
    is_operational boolean NOT NULL,
    latency_ms integer NOT NULL,
    probe_status character varying(16) NOT NULL
);


--
-- Name: probe_event_probe_event_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.probe_event_probe_event_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: probe_event_probe_event_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.probe_event_probe_event_key_seq OWNED BY public.probe_event.probe_event_key;


--
-- Name: problem; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.problem (
    problem_key bigint NOT NULL,
    problem_fingerprint character varying(16) NOT NULL,
    problem_exception jsonb,
    reported_at timestamp with time zone DEFAULT now() NOT NULL,
    dispatched_at timestamp with time zone,
    resolved_at timestamp with time zone,
    problem_type character varying(500) NOT NULL,
    problem_title character varying(500) NOT NULL,
    problem_status integer,
    problem_detail text,
    problem_instance character varying(2048),
    problem_extensions jsonb,
    app_key integer NOT NULL,
    environment_key integer NOT NULL,
    account_id uuid,
    account_email character varying(254),
    app_version character varying(100)
);


--
-- Name: COLUMN problem.app_version; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.problem.app_version IS 'Version the reporting process declared for itself, e.g. 1.3.174+db890ee. NULL when the reporter sent none; readers then show the app registry version, which is the last build cut rather than the build that threw.';


--
-- Name: problem_problem_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.problem_problem_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: problem_problem_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.problem_problem_key_seq OWNED BY public.problem.problem_key;


--
-- Name: server; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.server (
    server_key integer NOT NULL,
    server_number smallint,
    server_handle character varying(60) NOT NULL,
    server_name character varying(100) NOT NULL,
    server_description character varying(500),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: server_server_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.server_server_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: server_server_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.server_server_key_seq OWNED BY public.server.server_key;


--
-- Name: service; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.service (
    service_key integer NOT NULL,
    service_handle character varying(60) NOT NULL,
    service_name character varying(100) NOT NULL,
    service_url character varying(2048) NOT NULL,
    service_paused boolean DEFAULT false NOT NULL,
    site_id integer,
    owner_key integer NOT NULL,
    environment_key integer NOT NULL,
    app_key integer,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: service_daily; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.service_daily (
    service_key integer NOT NULL,
    day date NOT NULL,
    probes integer DEFAULT 0 NOT NULL,
    operational_count integer DEFAULT 0 NOT NULL,
    latency_sum_ms bigint DEFAULT 0 NOT NULL
);


--
-- Name: service_service_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.service_service_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: service_service_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.service_service_key_seq OWNED BY public.service.service_key;


--
-- Name: service_state; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.service_state (
    service_key integer NOT NULL,
    history_json jsonb DEFAULT '[]'::jsonb NOT NULL,
    latency_ms integer DEFAULT 0 NOT NULL,
    uptime_pct numeric(5,2) DEFAULT 100.00 NOT NULL,
    last_status character varying(16) DEFAULT 'operational'::character varying NOT NULL,
    last_check_at timestamp with time zone,
    last_outage_at timestamp with time zone
);


--
-- Name: subscriber; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscriber (
    subscriber_key integer NOT NULL,
    subscriber_email character varying(254) NOT NULL,
    owner_key integer NOT NULL,
    confirm_token bytea NOT NULL,
    unsubscribe_token bytea NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    confirmed_at timestamp with time zone
);


--
-- Name: subscriber_subscriber_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.subscriber_subscriber_key_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: subscriber_subscriber_key_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.subscriber_subscriber_key_seq OWNED BY public.subscriber.subscriber_key;


--
-- Name: account_email_change change_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_email_change ALTER COLUMN change_key SET DEFAULT nextval('public.account_email_change_change_key_seq'::regclass);


--
-- Name: account_password_change change_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_password_change ALTER COLUMN change_key SET DEFAULT nextval('public.account_password_change_change_key_seq'::regclass);


--
-- Name: account_recovery recovery_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_recovery ALTER COLUMN recovery_key SET DEFAULT nextval('public.account_recovery_recovery_key_seq'::regclass);


--
-- Name: announcement announcement_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.announcement ALTER COLUMN announcement_key SET DEFAULT nextval('public.announcement_announcement_key_seq'::regclass);


--
-- Name: app app_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app ALTER COLUMN app_key SET DEFAULT nextval('public.app_app_key_seq'::regclass);


--
-- Name: environment environment_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.environment ALTER COLUMN environment_key SET DEFAULT nextval('public.environment_environment_key_seq'::regclass);


--
-- Name: idempotency idempotency_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.idempotency ALTER COLUMN idempotency_key SET DEFAULT nextval('public.idempotency_idempotency_key_seq'::regclass);


--
-- Name: outage outage_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage ALTER COLUMN outage_key SET DEFAULT nextval('public.outage_outage_key_seq'::regclass);


--
-- Name: outage_update update_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage_update ALTER COLUMN update_key SET DEFAULT nextval('public.outage_update_update_key_seq'::regclass);


--
-- Name: owner owner_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.owner ALTER COLUMN owner_key SET DEFAULT nextval('public.owner_owner_key_seq'::regclass);


--
-- Name: probe_event probe_event_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.probe_event ALTER COLUMN probe_event_key SET DEFAULT nextval('public.probe_event_probe_event_key_seq'::regclass);


--
-- Name: problem problem_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.problem ALTER COLUMN problem_key SET DEFAULT nextval('public.problem_problem_key_seq'::regclass);


--
-- Name: server server_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.server ALTER COLUMN server_key SET DEFAULT nextval('public.server_server_key_seq'::regclass);


--
-- Name: service service_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service ALTER COLUMN service_key SET DEFAULT nextval('public.service_service_key_seq'::regclass);


--
-- Name: subscriber subscriber_key; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscriber ALTER COLUMN subscriber_key SET DEFAULT nextval('public.subscriber_subscriber_key_seq'::regclass);


--
-- Name: _migration_history _migration_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public._migration_history
    ADD CONSTRAINT _migration_history_pkey PRIMARY KEY (name);


--
-- Name: account_email_change account_email_change_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_email_change
    ADD CONSTRAINT account_email_change_pkey PRIMARY KEY (change_key);


--
-- Name: account_password_change account_password_change_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_password_change
    ADD CONSTRAINT account_password_change_pkey PRIMARY KEY (change_key);


--
-- Name: account account_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account
    ADD CONSTRAINT account_pkey PRIMARY KEY (account_id);


--
-- Name: account_recovery account_recovery_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_recovery
    ADD CONSTRAINT account_recovery_pkey PRIMARY KEY (recovery_key);


--
-- Name: account_session account_session_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_session
    ADD CONSTRAINT account_session_pkey PRIMARY KEY (session_id);


--
-- Name: announcement announcement_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.announcement
    ADD CONSTRAINT announcement_pkey PRIMARY KEY (announcement_key);


--
-- Name: app app_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app
    ADD CONSTRAINT app_pkey PRIMARY KEY (app_key);


--
-- Name: environment environment_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.environment
    ADD CONSTRAINT environment_pkey PRIMARY KEY (environment_key);


--
-- Name: idempotency idempotency_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.idempotency
    ADD CONSTRAINT idempotency_pkey PRIMARY KEY (idempotency_key);


--
-- Name: outage outage_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage
    ADD CONSTRAINT outage_pkey PRIMARY KEY (outage_key);


--
-- Name: outage_update outage_update_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage_update
    ADD CONSTRAINT outage_update_pkey PRIMARY KEY (update_key);


--
-- Name: owner owner_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.owner
    ADD CONSTRAINT owner_pkey PRIMARY KEY (owner_key);


--
-- Name: probe_event probe_event_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.probe_event
    ADD CONSTRAINT probe_event_pkey PRIMARY KEY (probe_event_key);


--
-- Name: problem problem_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.problem
    ADD CONSTRAINT problem_pkey PRIMARY KEY (problem_key);


--
-- Name: server server_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.server
    ADD CONSTRAINT server_pkey PRIMARY KEY (server_key);


--
-- Name: service_daily service_daily_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_daily
    ADD CONSTRAINT service_daily_pkey PRIMARY KEY (service_key, day);


--
-- Name: service service_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service
    ADD CONSTRAINT service_pkey PRIMARY KEY (service_key);


--
-- Name: service_state service_state_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_state
    ADD CONSTRAINT service_state_pkey PRIMARY KEY (service_key);


--
-- Name: subscriber subscriber_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscriber
    ADD CONSTRAINT subscriber_pkey PRIMARY KEY (subscriber_key);


--
-- Name: ix_account_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_account_email ON public.account USING btree (lower((account_email)::text));


--
-- Name: ix_account_email_change_account; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_account_email_change_account ON public.account_email_change USING btree (account_id);


--
-- Name: ix_account_email_change_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_account_email_change_hash ON public.account_email_change USING btree (change_hash);


--
-- Name: ix_account_password_change_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_account_password_change_hash ON public.account_password_change USING btree (change_hash);


--
-- Name: ix_account_recovery_account; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_account_recovery_account ON public.account_recovery USING btree (account_id);


--
-- Name: ix_account_session_account; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_account_session_account ON public.account_session USING btree (account_id);


--
-- Name: ix_account_session_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_account_session_active ON public.account_session USING btree (expires_at) WHERE (revoked_at IS NULL);


--
-- Name: ix_account_session_token; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_account_session_token ON public.account_session USING btree (bearer_token_hash);


--
-- Name: ix_announcement_owner; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_announcement_owner ON public.announcement USING btree (owner_key);


--
-- Name: ix_announcement_pending; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_announcement_pending ON public.announcement USING btree (publish_at) WHERE (dispatched_at IS NULL);


--
-- Name: ix_announcement_publish_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_announcement_publish_at ON public.announcement USING btree (publish_at);


--
-- Name: ix_app_handle; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_app_handle ON public.app USING btree (app_handle);


--
-- Name: ix_environment_handle; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_environment_handle ON public.environment USING btree (environment_handle);


--
-- Name: ix_environment_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_environment_number ON public.environment USING btree (environment_number);


--
-- Name: ix_idempotency_expires; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_idempotency_expires ON public.idempotency USING btree (expires_at);


--
-- Name: ix_idempotency_lookup; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_idempotency_lookup ON public.idempotency USING btree (bearer_token_hash, idempotency_code);


--
-- Name: ix_outage_open_by_svc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_outage_open_by_svc ON public.outage USING btree (service_key) WHERE ((outage_status)::text <> 'resolved'::text);


--
-- Name: ix_outage_service; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_outage_service ON public.outage USING btree (service_key);


--
-- Name: ix_outage_started; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_outage_started ON public.outage USING btree (started_at DESC);


--
-- Name: ix_outage_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_outage_status ON public.outage USING btree (outage_status);


--
-- Name: ix_outage_update_outage; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_outage_update_outage ON public.outage_update USING btree (outage_key, created_at);


--
-- Name: ix_owner_handle; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_owner_handle ON public.owner USING btree (owner_handle);


--
-- Name: ix_owner_host; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_owner_host ON public.owner USING btree (lower((owner_host)::text));


--
-- Name: ix_owner_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_owner_number ON public.owner USING btree (owner_number);


--
-- Name: ix_probe_event_service_checked; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_probe_event_service_checked ON public.probe_event USING btree (service_key, checked_at DESC);


--
-- Name: ix_problem_app; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_problem_app ON public.problem USING btree (app_key);


--
-- Name: ix_problem_dispatched; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_problem_dispatched ON public.problem USING btree (problem_fingerprint) WHERE (dispatched_at IS NULL);


--
-- Name: ix_problem_environment; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_problem_environment ON public.problem USING btree (environment_key);


--
-- Name: ix_problem_fingerprint; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_problem_fingerprint ON public.problem USING btree (problem_fingerprint);


--
-- Name: ix_problem_reported_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_problem_reported_at ON public.problem USING btree (reported_at DESC);


--
-- Name: ix_problem_unresolved; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_problem_unresolved ON public.problem USING btree (reported_at DESC) WHERE (resolved_at IS NULL);


--
-- Name: ix_server_handle; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_server_handle ON public.server USING btree (server_handle);


--
-- Name: ix_server_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_server_number ON public.server USING btree (server_number);


--
-- Name: ix_service_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_service_active ON public.service USING btree (service_paused) WHERE (service_paused = false);


--
-- Name: ix_service_app; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_service_app ON public.service USING btree (app_key);


--
-- Name: ix_service_daily_day; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_service_daily_day ON public.service_daily USING btree (day);


--
-- Name: ix_service_env; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_service_env ON public.service USING btree (environment_key);


--
-- Name: ix_service_handle; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_service_handle ON public.service USING btree (service_handle);


--
-- Name: ix_service_owner; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_service_owner ON public.service USING btree (owner_key);


--
-- Name: ix_subscriber_confirm_tok; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_subscriber_confirm_tok ON public.subscriber USING btree (confirm_token);


--
-- Name: ix_subscriber_confirmed; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_subscriber_confirmed ON public.subscriber USING btree (owner_key) WHERE (confirmed_at IS NOT NULL);


--
-- Name: ix_subscriber_owner_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_subscriber_owner_email ON public.subscriber USING btree (owner_key, lower((subscriber_email)::text));


--
-- Name: ix_subscriber_unsub_tok; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_subscriber_unsub_tok ON public.subscriber USING btree (unsubscribe_token);


--
-- Name: account_email_change account_email_change_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_email_change
    ADD CONSTRAINT account_email_change_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.account(account_id) ON DELETE CASCADE;


--
-- Name: account_password_change account_password_change_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_password_change
    ADD CONSTRAINT account_password_change_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.account(account_id) ON DELETE CASCADE;


--
-- Name: account_recovery account_recovery_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_recovery
    ADD CONSTRAINT account_recovery_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.account(account_id) ON DELETE CASCADE;


--
-- Name: account_session account_session_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_session
    ADD CONSTRAINT account_session_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.account(account_id) ON DELETE CASCADE;


--
-- Name: announcement announcement_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.announcement
    ADD CONSTRAINT announcement_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.account(account_id) ON DELETE SET NULL;


--
-- Name: announcement announcement_owner_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.announcement
    ADD CONSTRAINT announcement_owner_key_fkey FOREIGN KEY (owner_key) REFERENCES public.owner(owner_key) ON DELETE CASCADE;


--
-- Name: outage outage_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage
    ADD CONSTRAINT outage_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.account(account_id) ON DELETE SET NULL;


--
-- Name: outage outage_service_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage
    ADD CONSTRAINT outage_service_key_fkey FOREIGN KEY (service_key) REFERENCES public.service(service_key) ON DELETE SET NULL;


--
-- Name: outage_update outage_update_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage_update
    ADD CONSTRAINT outage_update_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.account(account_id) ON DELETE SET NULL;


--
-- Name: outage_update outage_update_outage_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outage_update
    ADD CONSTRAINT outage_update_outage_key_fkey FOREIGN KEY (outage_key) REFERENCES public.outage(outage_key) ON DELETE CASCADE;


--
-- Name: probe_event probe_event_service_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.probe_event
    ADD CONSTRAINT probe_event_service_key_fkey FOREIGN KEY (service_key) REFERENCES public.service(service_key) ON DELETE CASCADE;


--
-- Name: problem problem_app_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.problem
    ADD CONSTRAINT problem_app_key_fkey FOREIGN KEY (app_key) REFERENCES public.app(app_key) ON DELETE CASCADE;


--
-- Name: problem problem_environment_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.problem
    ADD CONSTRAINT problem_environment_key_fkey FOREIGN KEY (environment_key) REFERENCES public.environment(environment_key) ON DELETE CASCADE;


--
-- Name: service service_app_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service
    ADD CONSTRAINT service_app_key_fkey FOREIGN KEY (app_key) REFERENCES public.app(app_key) ON DELETE SET NULL;


--
-- Name: service_daily service_daily_service_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_daily
    ADD CONSTRAINT service_daily_service_key_fkey FOREIGN KEY (service_key) REFERENCES public.service(service_key) ON DELETE CASCADE;


--
-- Name: service service_environment_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service
    ADD CONSTRAINT service_environment_key_fkey FOREIGN KEY (environment_key) REFERENCES public.environment(environment_key) ON DELETE CASCADE;


--
-- Name: service service_owner_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service
    ADD CONSTRAINT service_owner_key_fkey FOREIGN KEY (owner_key) REFERENCES public.owner(owner_key) ON DELETE CASCADE;


--
-- Name: service_state service_state_service_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_state
    ADD CONSTRAINT service_state_service_key_fkey FOREIGN KEY (service_key) REFERENCES public.service(service_key) ON DELETE CASCADE;


--
-- Name: subscriber subscriber_owner_key_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscriber
    ADD CONSTRAINT subscriber_owner_key_fkey FOREIGN KEY (owner_key) REFERENCES public.owner(owner_key) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict CRxbzsA8jyEYt9KXAEoZwUY9T0PRx7A4Fuu6x3OZDX0x4yUWcqti6mPTWoX7DtM

