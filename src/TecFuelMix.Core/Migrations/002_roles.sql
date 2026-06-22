do $$
begin
    if not exists (select 1 from pg_roles where rolname = 'fuelmix_writer') then
        create role fuelmix_writer;
    end if;

    if not exists (select 1 from pg_roles where rolname = 'fuelmix_reader') then
        create role fuelmix_reader;
    end if;
end
$$;

grant usage on schema public to fuelmix_writer, fuelmix_reader;

grant select, insert, update, delete
    on table fuel_mix_snapshots, fuel_mix_readings, ingestion_runs
    to fuelmix_writer;

grant usage, select
    on sequence fuel_mix_snapshots_id_seq, ingestion_runs_id_seq
    to fuelmix_writer;

grant select
    on table fuel_mix_snapshots, fuel_mix_readings
    to fuelmix_reader;

alter default privileges in schema public
    grant select, insert, update, delete on tables to fuelmix_writer;

alter default privileges in schema public
    grant usage, select on sequences to fuelmix_writer;

alter default privileges in schema public
    grant select on tables to fuelmix_reader;
