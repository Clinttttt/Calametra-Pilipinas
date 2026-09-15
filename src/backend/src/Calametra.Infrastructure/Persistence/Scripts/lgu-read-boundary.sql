-- ── THE READ BOUNDARY, LAYER 2: DATABASE PERMISSIONS ─────────────────────────
--
-- ADR-005 D4 requires that no figure rendered to a reader is derived from an unreviewed
-- crosswalk proposal, and states three layers that fail independently:
--
--   1. a Confirmed-only view, and an analytics context that exposes no other read path
--   2. permissions on the base table, WHERE THE DEPLOYMENT ALLOWS SEPARATE ROLES  ← this file
--   3. tests asserting a proposal is invisible through every public read path
--
-- Layer 2 is deliberately qualified in the ADR. A single-role local deployment cannot separate
-- application reads from migration writes, and a decision that mandates what the environment
-- cannot supply is ignored rather than obeyed. So this script is idempotent, does nothing when
-- the roles do not exist, and never fails a deployment that has not adopted them.
--
-- Run as a superuser or the database owner, after `dotnet ef database update`:
--
--   docker exec -i calametra-db psql -U calametra -d calametra < lgu-read-boundary.sql
--
-- Whether it took effect is MEASURED rather than assumed: the readiness report queries
-- has_table_privilege and states the answer, so an operator who skipped this file sees that
-- layer 2 is inactive instead of believing it is on.

-- The role the API and the ingestion worker connect as when the deployment separates them.
-- Adjust the name to match the deployment; the block is a no-op if it does not exist.
DO $$
DECLARE
    analytics_role text := 'calametra_app';
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = analytics_role) THEN
        RAISE NOTICE
            'Role % does not exist. Layer 2 of the crosswalk read boundary is NOT active; the '
            'Confirmed-only view and the boundary tests are carrying it alone. This is an accepted '
            'configuration for a single-role deployment — see ADR-005 D4.', analytics_role;
        RETURN;
    END IF;

    -- The base table holds proposals. The application role must not be able to read it at all:
    -- that is the point of the layer. Revoked rather than never granted, because a role created by
    -- an earlier convenience grant would otherwise keep the access.
    EXECUTE format('REVOKE ALL ON TABLE lgu_code_links FROM %I', analytics_role);

    -- The view is the only crosswalk surface the application may read.
    EXECUTE format('GRANT SELECT ON TABLE lgu_code_links_confirmed TO %I', analytics_role);

    -- The register and the canonical units are ordinary reference data: a reader may see which
    -- edition a figure was reconciled to, and which unit a code names.
    EXECUTE format('GRANT SELECT ON TABLE lgus TO %I', analytics_role);
    EXECUTE format('GRANT SELECT ON TABLE psgc_register_editions TO %I', analytics_role);

    RAISE NOTICE
        'Layer 2 active: % cannot read lgu_code_links and may read lgu_code_links_confirmed.',
        analytics_role;
END
$$;
