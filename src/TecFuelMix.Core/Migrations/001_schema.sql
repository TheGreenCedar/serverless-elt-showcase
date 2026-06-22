create table if not exists fuel_mix_snapshots (
    id bigserial primary key,
    source_ref_id text not null unique,
    interval_est timestamp without time zone not null,
    total_mw numeric(12,3) not null,
    raw_payload jsonb not null,
    imported_at timestamptz not null default now()
);

create table if not exists fuel_mix_readings (
    snapshot_id bigint not null references fuel_mix_snapshots(id) on delete cascade,
    category text not null,
    mw numeric(12,3) not null,
    source_label text not null,
    primary key (snapshot_id, category)
);

create table if not exists ingestion_runs (
    id bigserial primary key,
    started_at timestamptz not null default now(),
    completed_at timestamptz,
    status text not null,
    source_ref_id text,
    error_message text
);

create index if not exists ix_fuel_mix_snapshots_interval_est
    on fuel_mix_snapshots (interval_est desc);

create index if not exists ix_fuel_mix_readings_category_snapshot
    on fuel_mix_readings (category, snapshot_id);
