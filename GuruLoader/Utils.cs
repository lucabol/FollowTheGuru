using System.Net.Http;
using System.Threading.Tasks;

static class Utils {
    public async static Task<string> HttpLoadAsync(string page) {

        using (HttpClient client = new HttpClient())
        using (HttpResponseMessage response = await client.GetAsync(page))
        using (HttpContent content = response.Content)
            return await content.ReadAsStringAsync();
    }
}