-- Contact details associated with customer profiles.
SET @CONTACT_NORTH := 400101;
SET @CONTACT_SOUTH := 400102;

CREATE TABLE IF NOT EXISTS `cp_profile_contact` (
  `entry` INT UNSIGNED NOT NULL,
  `name` VARCHAR(64) NOT NULL,
  `profile_entry` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

DELETE FROM `cp_profile_contact` WHERE `entry` IN (@CONTACT_NORTH, @CONTACT_SOUTH);

INSERT INTO `cp_profile_contact` (`entry`, `name`, `profile_entry`) VALUES
  (@CONTACT_NORTH, 'Operations Contact', 400001),
  (@CONTACT_SOUTH, 'Billing Contact', 400002);
