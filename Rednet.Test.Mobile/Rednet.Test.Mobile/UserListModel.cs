using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Rednet.Test.Mobile.Objects;
using Xamarin.Forms;

namespace Rednet.Test.Mobile
{
    public class UserListModel
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<User> UserList { get; set; }

        public UserListModel()
        {
            this.UserList = new ObservableCollection<User>();

            // here, when we dont put any parameters on User.Query() method, all the rows are returned
            foreach (var _user in User.Query())
            {
                this.UserList.Add(_user);
            }

            this.BackCommand = new Command(async () => { await App.Current.MainPage.Navigation.PopModalAsync(); });
        }

        public ICommand BackCommand { get; set; }
    }
}