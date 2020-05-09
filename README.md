# Webcam Barcode Reader
The samples demonstrate how to combine [Dynamsoft Barcode Reader](https://www.dynamsoft.com/Products/Dynamic-Barcode-Reader.aspx) and OpenCV to build webcam barcode scan apps using C++, Java, and C# on Windows.

## License
Get a [FREE 30-day trial license](https://www.dynamsoft.com/CustomerPortal/Portal/Triallicense.aspx).

## C++

1. Set a valid license:

    ```c++
    iRet = reader.InitLicense("LICENSE-KEY");
    ```
2. Set barcode types:

    ```c++
    // Decode all supported barcode types.
    runtimeSettings.barcodeFormatIds = BF_ALL;
	runtimeSettings.barcodeFormatIds_2 = BF2_POSTALCODE | BF2_DOTCODE;
    ```

3. Build and run the app:

    ```
    mkdir build
    cd build
    cmake -G"Visual Studio 15 2017 Win64" ..
    cmake --build .
    .\debug\BarcodeReader.exe
    ```

    ![DotCode c++](http://www.codepool.biz/wp-content/uploads/2020/05/dotcode-cplusplus.png)

## Java
1. Set a valid license:

    ```c++
    mBarcodeReader = new BarcodeReader("LICENSE-KEY");
    ```

2. Install OpenCV jar files to local Maven repository:

    ```
    mvn install:install-file -Dfile=opencv-430.jar -DgroupId=org -DartifactId=opencv -Dversion=4.3.0 -Dpackaging=jar
    ```

3. Set barcode types:

    ```java
    // Only decode DotCode
    runtimeSettings.barcodeFormatIds = EnumBarcodeFormat.BF_NULL; 
    runtimeSettings.barcodeFormatIds_2 = EnumBarcodeFormat_2.BF2_DOTCODE;
    ```

4. Build and run the project:

    ```
    mvn clean install assembly:assembly -Dmaven.test.skip=true
    java -cp target/opencv-dotcode-1.0-SNAPSHOT-jar-with-dependencies.jar com.java.barcode.App
    ```

    ![DotCode Java](https://www.codepool.biz/wp-content/uploads/2020/04/java-dotcode-reader.png)

## C#
1. Import the project to `Visual Studio`.
2. Set a valid license in the `App.config` file.

    ```xml
    <appSettings>
        <add key="DBRLicense" value="LICNSE-KEY"/>
    </appSettings>
    ```

3. Press `F5` to run the app.

    ![DotCode C#](http://www.codepool.biz/wp-content/uploads/2020/05/dotcode-dotnet.png)


## Contact Us
https://www.dynamsoft.com/Company/Contact.aspx

## License Agreement
https://www.dynamsoft.com/Products/barcode-reader-license-agreement.aspx
