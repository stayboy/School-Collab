using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCodedValuesHistoryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop trigger first — function body references wrong columns
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_coded_values_history ON coded_values;");

            // Rename history-table columns to match the actual coded_values schema.
            // These RENAMEs are no-ops if the column already has the correct name.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'coded_values_history' AND column_name = 'display_name') THEN
                        ALTER TABLE coded_values_history RENAME COLUMN display_name TO name;
                    END IF;

                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'coded_values_history' AND column_name = 'sort_order') THEN
                        ALTER TABLE coded_values_history RENAME COLUMN sort_order TO display_order;
                    END IF;

                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'coded_values_history' AND column_name = 'source_code') THEN
                        ALTER TABLE coded_values_history DROP COLUMN source_code;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_name = 'coded_values_history' AND column_name = 'created_at') THEN
                        ALTER TABLE coded_values_history ADD COLUMN created_at TIMESTAMPTZ;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_name = 'coded_values_history' AND column_name = 'updated_at') THEN
                        ALTER TABLE coded_values_history ADD COLUMN updated_at TIMESTAMPTZ;
                    END IF;
                END;
                $$;
                """);

            // Recreate the trigger function with column names matching coded_values
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

            // Re-attach trigger
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

            // Revert column renames (idempotent)
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'coded_values_history' AND column_name = 'name') THEN
                        ALTER TABLE coded_values_history RENAME COLUMN name TO display_name;
                    END IF;

                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'coded_values_history' AND column_name = 'display_order') THEN
                        ALTER TABLE coded_values_history RENAME COLUMN display_order TO sort_order;
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE coded_values_history ADD COLUMN IF NOT EXISTS source_code TEXT;
                ALTER TABLE coded_values_history DROP COLUMN IF EXISTS created_at;
                ALTER TABLE coded_values_history DROP COLUMN IF EXISTS updated_at;
                """);
        }
    }
}
