# Production Posture

## RDS Durability

The Terraform defaults are production-shaped:

| Setting | Default | Why |
| --- | --- | --- |
| `rds_deletion_protection` | `true` | Prevents accidental database deletion. |
| `rds_skip_final_snapshot` | `false` | Keeps a final restore point on delete. |
| `rds_backup_retention_days` | `7` | Enables automated point-in-time recovery coverage. |
| `storage_encrypted` | `true` | Encrypts the RDS storage volume. |

For disposable challenge teardown, override the safety knobs in `infra/terraform/terraform.tfvars` before destroying:

```hcl
rds_deletion_protection = false
rds_skip_final_snapshot = true
rds_backup_retention_days = 1
```

If final snapshots are enabled, set `rds_final_snapshot_identifier` to a unique name before destroy if the default snapshot name already exists.

## Secrets And State

Terraform `sensitive = true` only suppresses casual CLI display. It does not keep applied secret values out of Terraform state. This submission keeps secrets out of the repository and writes them to AWS Secrets Manager, but the Terraform state still needs production handling:

- store state in an encrypted remote backend;
- restrict state read/write access to the smallest deployment operator set;
- audit state access through the backend and cloud account logs;
- rotate database and bearer credentials if state access is exposed;
- prefer generated or externally managed secret values where the deployment workflow allows it.

This follow-up does not add automated secret rotation. A production rotation design should cover PostgreSQL user password rotation, RDS Proxy credential refresh, Lambda read timing during rotation, and an application smoke test after cutover.
