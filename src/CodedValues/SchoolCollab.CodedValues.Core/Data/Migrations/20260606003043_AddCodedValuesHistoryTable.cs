using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCodedValuesHistoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create history table mirroring coded_values with audit columns.
            // "xmin" is a PostgreSQL system column so we use "row_xmin" instead.
            // Column names must match the actual coded_values table exactly.
            migrationBuilder.Sql("""
                CREATE TABLE coded_values_history (
                    history_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    operation         TEXT NOT NULL CHECK (operation IN ('INSERT','UPDATE','DELETE')),
                    operated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
                    operated_by       TEXT,
                    id                UUID NOT NULL,
                    parent_id         UUID,
                    code              TEXT NOT NULL,
                    name              TEXT,
                    description       TEXT,
                    display_order     INTEGER,
                    is_disabled       BOOLEAN NOT NULL DEFAULT FALSE,
                    is_deleted        BOOLEAN NOT NULL DEFAULT FALSE,
                    deleted_at        TIMESTAMPTZ,
                    created_at        TIMESTAMPTZ,
                    updated_at        TIMESTAMPTZ,
                    row_xmin          XID NOT NULL
                );
                """);

            // Create indexes for point-in-time queries
            migrationBuilder.Sql("""
                CREATE INDEX ix_coded_values_history_id ON coded_values_history (id);
                CREATE INDEX ix_coded_values_history_operated_at ON coded_values_history (operated_at);
                """);

            // Trigger function for INSERT/UPDATE/DELETE — captures row state into history.
            // OLD.xmin / NEW.xmin reads the PostgreSQL system column at trigger time.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION coded_values_history_audit() RETURNS TRIGGER AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        INSERT INTO coded_values_history (operation, operated_by, id, parent_id, code, name, description, display_order, is_disabled, is_deleted, deleted_at, created_at, updated_at, row_xmin)
                        VALUES ('DELETE', current_user, OLD.id, OLD.parent_id, OLD.code, OLD.name, OLD.description, OLD.display_order, OLD.is_disabled, OLD.is_deleted, OLD.deleted_at, OLD.created_at, OLD.updated_at, OLD.xmin);
                        RETURN OLD;
                    ELSIF TG_OP = 'UPDATE' THEN
                        INSERT INTO coded_values_history (operation, operated_by, id, parent_id, code, name, description, display_order, is_disabled, is_deleted, deleted_at, created_at, updated_at, row_xmin)
                        VALUES ('UPDATE', current_user, NEW.id, NEW.parent_id, NEW.code, NEW.name, NEW.description, NEW.display_order, NEW.is_disabled, NEW.is_deleted, NEW.deleted_at, NEW.created_at, NEW.updated_at, NEW.xmin);
                        RETURN NEW;
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """);

            // Attach trigger to coded_values table
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_coded_values_history
                AFTER INSERT OR UPDATE OR DELETE ON coded_values
                FOR EACH ROW EXECUTE FUNCTION coded_values_history_audit();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_coded_values_history ON coded_values;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS coded_values_history_audit();");
            migrationBuilder.Sql("DROP TABLE IF EXISTS coded_values_history;");
        }
    }
}
