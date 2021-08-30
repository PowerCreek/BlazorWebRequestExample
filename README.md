# BlazorWebRequest Sample Project

**How to build**

1) Clone the repository

2) Build the **BasicBlazorExample** project so it creates the _Framework files.

3) Take the _Framework directory from the build location, and place it into the working directory of **BasicBlazorExampleWebApp** within the wwwroot folder.

4) Run the **BasicBlazorExampleWebApp** Project.


**BasicBlazorExample**
This is the blazor wasm client project


**basicblazorexamplewebapp**
This is the backend service that serves the wasm framework file and also routes the rest api requests.

The other project is a shared dependency for both projects to pull from.
