﻿using Windows.UI.Xaml.Controls;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace ConcentratorXmpp
{
	public sealed partial class RegistrationDialog : ContentDialog
	{
		public string Reg_Name = string.Empty;
		public string Reg_Room = string.Empty;
		public string Reg_Apartment = string.Empty;
		public string Reg_Building = string.Empty;
		public string Reg_Street = string.Empty;
		public string Reg_StreetNr = string.Empty;
		public string Reg_Area = string.Empty;
		public string Reg_City = string.Empty;
		public string Reg_Region = string.Empty;
		public string Reg_Country = string.Empty;

		public RegistrationDialog()
		{
			this.InitializeComponent();
		}

		private void ContentDialog_ConnectButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			this.Reg_Name = this.NameInput.Text;
			this.Reg_Room = this.RoomInput.Text;
			this.Reg_Apartment = this.ApartmentInput.Text;
			this.Reg_Building = this.BuildingInput.Text;
			this.Reg_Street = this.StreetInput.Text;
			this.Reg_StreetNr = this.StreetNrInput.Text;
			this.Reg_Area = this.AreaInput.Text;
			this.Reg_City = this.CityInput.Text;
			this.Reg_Region = this.RegionInput.Text;
			this.Reg_Country = this.CountryInput.Text;
		}

		private void ContentDialog_CancelButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
		}
	}
}
