-- Sources that can emit audit events.
SET @SOURCE_API := 500001;
SET @SOURCE_WORKER := 500002;

CREATE TABLE IF NOT EXISTS `ae_event_source` (
  `entry` INT UNSIGNED NOT NULL,
  `name` VARCHAR(64) NOT NULL,
  `retention_days` SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

DELETE FROM `ae_event_source` WHERE `entry` IN (@SOURCE_API, @SOURCE_WORKER);

INSERT INTO `ae_event_source` (`entry`, `name`, `retention_days`) VALUES
  (@SOURCE_API, 'Public API', 30),
  (@SOURCE_WORKER, 'Background Worker', 90);
